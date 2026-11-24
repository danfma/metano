using System.Text.RegularExpressions;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Parenthesization;
using Metano.Compiler.TypeScript.Parenthesization.Templates;

namespace Metano.Compiler.TypeScript;

/// <summary>
/// Walks a TypeScript AST and produces formatted source text.
/// Modeled after the tsc emitter: AST in, text out.
/// </summary>
public sealed class Printer(string indent = "  ")
{
    /// <summary>The annotation bundlers read as "no side effects, drop when unused".</summary>
    private const string PureAnnotation = "/* @__PURE__ */ ";

    /// <summary>The one object literal key that is not a plain property.</summary>
    private const string ProtoKey = "__proto__";

    private readonly IndentedStringBuilder _sb = new(indent);

    /// <summary>
    /// Prints a whole module: rendering keeps every runtime helper name in
    /// its deferred form (<see cref="DeferredHelperNames"/>) and this pass
    /// resolves each wrap to the file's alias — the local binding the import
    /// line emitted for a colliding helper (issue #484) — or back to the
    /// plain name. Resolving here (not during rendering) is what lets one
    /// instance print many files without per-file state.
    /// </summary>
    public string Print(TsSourceFile file)
    {
        var rendered = PrintWithDeferredHelperNames(file);

        return DeferredHelperNames.Resolve(rendered, file.RuntimeHelperAliases);
    }

    /// <summary>
    /// Renders the file with every runtime helper name still in its deferred
    /// form. The fragment bakers (<c>IrToTsStatementBridge.WrapLoop</c>
    /// headers, raw statement bodies) call it so the deferred names survive
    /// into the raw statement text and <see cref="Print"/> — which knows the
    /// file's aliases — resolves them.
    /// </summary>
    internal string PrintWithDeferredHelperNames(TsSourceFile file)
    {
        _sb.Clear();
        for (var i = 0; i < file.Statements.Count; i++)
        {
            var stmt = file.Statements[i];
            if (i > 0)
            {
                var prev = file.Statements[i - 1];
                if (NeedsBlankLineBetween(prev, stmt))
                    _sb.WriteLn();
            }
            PrintTopLevel(stmt);
            _sb.WriteLn();
        }
        return _sb.ToString();
    }

    /// <summary>
    /// How a runtime helper call prints: always deferred —
    /// <see cref="Print"/> resolves the wrap to the file's alias when the
    /// name collided (issue #484), or back to the plain name otherwise.
    /// </summary>
    private static string HelperName(string name) => DeferredHelperNames.Wrap(name);

    /// <summary>
    /// Renders a single <see cref="TsType"/> to text. Used by bridges
    /// that need to embed a type fragment inside a synthesized raw
    /// statement (e.g., the <c>[ObjectArgs]</c> destructuring header).
    /// Helper-bound type names come out in their deferred form
    /// (<see cref="DeferredHelperNames"/>) — the caller must embed the text
    /// in a statement that reaches <see cref="Print"/>, which resolves them.
    /// </summary>
    public string PrintType(string indent, TsType type)
    {
        _sb.Clear();
        PrintType(type);
        return _sb.GetRawString();
    }

    /// <summary>
    /// Renders a single <see cref="TsExpression"/> to text. Used by
    /// bridges that need to embed an expression fragment inside a
    /// synthesized raw statement. Runtime helper names come out in their
    /// deferred form (<see cref="DeferredHelperNames"/>) — the caller must
    /// embed the text in a statement that reaches <see cref="Print"/>, which
    /// resolves them; resolving manually would need
    /// <c>DeferredHelperNames.Resolve(text, null)</c>.
    /// </summary>
    public string PrintExpressionFragment(TsExpression expression)
    {
        _sb.Clear();
        PrintExpression(expression);
        return _sb.GetRawString();
    }

    private string PrintTypeOnce(TsType type)
    {
        _sb.Clear();
        PrintType(type);
        return _sb.GetRawString();
    }

    /// <summary>
    /// Decides if a blank line should separate two consecutive top-level statements.
    /// Imports/re-exports are grouped together with no blank lines. Distinct kinds
    /// (types vs functions vs constants) get a blank line between them. Same-kind
    /// items (e.g., consecutive functions) also get a blank line for readability.
    /// For ModuleEntryPoint bodies — where both sides are <see cref="TsTopLevelStatement"/>
    /// wrapping inner statements — the rule delegates to
    /// <see cref="ShouldInsertBlankBefore"/> so the same "conditionals /
    /// return / multi-line var decls are separated; simple consecutive vars are
    /// not" rule applies uniformly.
    /// </summary>
    private static bool NeedsBlankLineBetween(TsTopLevel prev, TsTopLevel next)
    {
        // Import / re-export block: no blank line between them, but blank line before
        // anything else.
        var prevIsImport = prev is TsImport or TsReExport;
        var nextIsImport = next is TsImport or TsReExport;
        if (prevIsImport && nextIsImport)
            return false;
        if (prevIsImport != nextIsImport)
            return true;

        // Both are [ModuleEntryPoint] body statements — apply the statement-level
        // spacing rule so consecutive `const` vars don't get spuriously separated,
        // but everything else at the top level still gets air (each route handler
        // call, each class instantiation, etc. is a distinct logical unit).
        if (prev is TsTopLevelStatement prevStmt && next is TsTopLevelStatement nextStmt)
            return ShouldInsertBlankBefore(prevStmt.Inner, nextStmt.Inner, topLevel: true);

        // For non-imports, separate everything else by blank lines (types, functions,
        // constants, classes, mixed top-level statement + non-statement).
        return true;
    }

    /// <summary>
    /// Encodes the statement-level blank-line rule used by
    /// <see cref="PrintStatementList"/> and <see cref="NeedsBlankLineBetween"/>:
    /// <list type="bullet">
    ///   <item>Always one blank line around control-flow (<c>if</c> / <c>switch</c>)
    ///   and before <c>return</c>. No loop nodes exist in the AST today; extend
    ///   <see cref="IsControlFlow"/> when they're added.</item>
    ///   <item>Always one blank line around a variable declaration whose initializer
    ///   prints multi-line (see <see cref="IsMultiLineVarDecl"/>).</item>
    ///   <item>No separator between consecutive single-line variable declarations
    ///   or between a variable declaration and a trivial expression statement.</item>
    /// </list>
    /// The rule is symmetric — if either side is a control-flow statement or a
    /// multi-line variable, the pair is separated.
    /// </summary>
    private static bool ShouldInsertBlankBefore(
        TsStatement prev,
        TsStatement curr,
        bool topLevel = false
    )
    {
        // Blank before any `return` (unless it's the first statement, which is
        // the caller's responsibility to skip).
        if (curr is TsReturnStatement)
            return true;

        // Control-flow structures get visual air on both sides.
        if (IsControlFlow(prev) || IsControlFlow(curr))
            return true;

        // Multi-line variable declarations get blank lines above and below so the
        // block shape is visually detached from its siblings.
        if (IsMultiLineVarDecl(prev) || IsMultiLineVarDecl(curr))
            return true;

        // Top-level statements (ModuleEntryPoint) are separated by default —
        // each route handler, middleware registration, etc. is a logical unit.
        // The only exception is consecutive single-line variable declarations
        // (`const app = ...; const store = ...;`) which stay grouped.
        if (topLevel && !(prev is TsVariableDeclaration && curr is TsVariableDeclaration))
            return true;

        return false;
    }

    /// <summary>
    /// The set of statements that always get a blank line around them.
    /// The AST currently has no loop statement nodes (for / while / foreach);
    /// if they're added in the future, extend this predicate.
    /// </summary>
    private static bool IsControlFlow(TsStatement stmt) =>
        stmt is TsIfStatement or TsSwitchStatement or TsBlockStatement;

    /// <summary>
    /// Whether <paramref name="initializer"/> is a call or <c>new</c>, the initializers
    /// a static field or a module variable of C# static state prints as
    /// <c>/* @__PURE__ */</c>: they are part of C#'s type initialization, which never
    /// runs for a type no code touches, so a bundle that drops the type drops them
    /// with it (spec 011 FR-043), and one that keeps the type runs them at load, as
    /// before. The annotation is part of the node's print, never comment trivia.
    /// </summary>
    private static bool IsPureInitializer(TsExpression? initializer) =>
        initializer is TsCallExpression or TsNewExpression;

    /// <summary>
    /// A static field whose initializer is pure-annotated, printed without its type
    /// annotation: tsgo drops a comment that follows a property's type annotation,
    /// and Biome removes the parentheses that would protect it. The declared type
    /// becomes an assertion (<c>= /* @__PURE__ */ new Ctx() as Ctx</c>), which keeps
    /// it for a later assignment; a <c>unique symbol</c> is inferred from the
    /// <c>Symbol()</c> call of a readonly static, and an assertion cannot spell it.
    /// </summary>
    private void PrintPureStaticFieldInitializer(TsExpression initializer, TsType type)
    {
        _sb.Write(" = ");
        _sb.Write(PureAnnotation);
        PrintExpression(initializer);

        if (type is TsUniqueSymbolType)
            return;

        _sb.Write(" as ");
        PrintType(type);
    }

    private void PrintFieldTypeAndInitializer(TsFieldMember field)
    {
        _sb.Write(": ");
        PrintType(field.Type);

        if (field.Initializer is null)
            return;

        _sb.Write(" = ");
        PrintExpression(field.Initializer);
    }

    private void PrintTry(TsTryStatement tryStmt)
    {
        _sb.Write("try");
        _sb.WriteBlock(() => PrintStatementList(tryStmt.Body));

        if (tryStmt.Catch is { } handler)
        {
            _sb.Write($" catch ({handler.Parameter})");
            _sb.WriteBlock(() => PrintStatementList(handler.Body));
        }

        if (tryStmt.Finally is { } finallyBody)
        {
            _sb.Write(" finally");
            _sb.WriteBlock(() => PrintStatementList(finallyBody));
        }
    }

    /// <summary>
    /// An expression is "complex" when it shouldn't appear inline inside an
    /// object literal property or array element because doing so produces an
    /// unreadably long line. Used by <see cref="ShouldPrintObjectMultiLine"/>
    /// and <see cref="ShouldPrintArrayMultiLine"/> to decide layout.
    /// </summary>
    private static bool IsComplexInlineExpression(TsExpression expr) =>
        expr
            is TsArrowFunction
                or TsArrayLiteral { Elements.Count: > 0 }
                or TsObjectLiteral { Properties.Count: > 0 };

    /// <summary>
    /// Whether <see cref="PrintObjectLiteral"/> would emit this object in
    /// block form. Shared by the printer and <see cref="IsMultiLineVarDecl"/>
    /// so the blank-line spacing rule stays in sync with the actual layout.
    /// </summary>
    private static bool ShouldPrintObjectMultiLine(TsObjectLiteral obj)
    {
        if (obj.Properties.Count > 3)
            return true;
        foreach (var prop in obj.Properties)
        {
            if (prop.Value is TsSpreadExpression)
                return true;
            if (IsComplexInlineExpression(prop.Value))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether the array-literal branch would emit this array in block form.
    /// Shared by the printer and <see cref="IsMultiLineVarDecl"/>.
    /// </summary>
    private static bool ShouldPrintArrayMultiLine(TsArrayLiteral arr) =>
        arr.Elements.Count > 0 && arr.Elements.Any(IsComplexInlineExpression);

    /// <summary>
    /// A variable declaration is "multi-line" when its initializer is a literal
    /// that the printer emits in block form — object literals that match
    /// <see cref="ShouldPrintObjectMultiLine"/> or array literals that match
    /// <see cref="ShouldPrintArrayMultiLine"/>. Everything else prints inline
    /// and does not need surrounding blank lines.
    /// </summary>
    private static bool IsMultiLineVarDecl(TsStatement stmt)
    {
        if (stmt is not TsVariableDeclaration varDecl)
            return false;
        return varDecl.Initializer switch
        {
            TsObjectLiteral obj => ShouldPrintObjectMultiLine(obj),
            TsArrayLiteral arr => ShouldPrintArrayMultiLine(arr),
            _ => false,
        };
    }

    /// <summary>
    /// Prints a list of statements (method / constructor / lambda / block body,
    /// or a switch case body) applying the statement-level blank-line rule from
    /// <see cref="ShouldInsertBlankBefore"/>. Replaces direct
    /// <c>_sb.WriteLines(list, PrintStatement)</c> calls, which would glue every
    /// statement together with a single newline regardless of kind.
    /// </summary>
    private void PrintStatementList(IReadOnlyList<TsStatement> statements)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            var curr = statements[i];
            if (i > 0 && ShouldInsertBlankBefore(statements[i - 1], curr))
                _sb.WriteLn();
            PrintStatement(curr);
            _sb.WriteLn();
        }
    }

    // ─── Top-level ──────────────────────────────────────────

    private void PrintTopLevel(TsTopLevel node)
    {
        switch (node)
        {
            case TsImport n:
                PrintImport(n);
                break;
            case TsReExport n:
                PrintReExport(n);
                break;
            case TsTypeAlias n:
                PrintTypeAlias(n);
                break;
            case TsInterface n:
                PrintInterface(n);
                break;
            case TsFunction n:
                PrintFunction(n);
                break;
            case TsConstObject n:
                PrintConstObject(n);
                break;
            case TsNamespaceDeclaration n:
                PrintNamespace(n);
                break;
            case TsClass n:
                PrintClass(n);
                break;
            case TsTopLevelStatement n:
                // Do NOT emit an extra WriteLn here — the `Print()` top-level
                // loop already writes a single trailing newline after every
                // PrintTopLevel call. Emitting one here produced the historical
                // double-blank-line bug in ModuleEntryPoint output
                // (`\n\n\n` between each statement in program.ts).
                PrintStatement(n.Inner);
                break;
            case TsModuleExport n:
                _sb.Write(n.IsDefault ? $"export default {n.Name};" : $"export {{ {n.Name} }};");
                _sb.WriteLn();
                break;
            case TsExportImportAlias n:
                // `export import Alias = Target;` — namespace-style
                // aliasing emitted by --namespace-barrels. Printed
                // wherever a TsExportImportAlias appears in the AST,
                // including file scope as well as inside a
                // `namespace { … }` block. Single-segment leaves
                // (`shared-kernel` → `$SharedKernel`) land at file
                // scope; multi-segment paths nest inside `export
                // namespace` blocks.
                _sb.Write($"export import {n.Alias} = {n.Target};");
                break;
        }
    }

    private void PrintImport(TsImport import)
    {
        _sb.Write("import ");
        if (import.TypeOnly)
            _sb.Write("type ");
        if (import.IsNamespace)
        {
            // `import * as Foo from "...";` — namespace import, single
            // alias in Names[0]. Used by the root barrel generated under
            // --namespace-barrels to bind a subpath's exports under a
            // local identifier so the outer namespace block can re-export
            // it via TsExportImportAlias.
            _sb.Write("* as ");
            _sb.Write(import.Names[0]);
        }
        else if (import.IsDefault)
        {
            // `import Foo from "...";` — single name, no braces.
            _sb.Write(import.Names[0]);
        }
        else
        {
            _sb.Write("{ ");
            // Per-name `type` qualifier and per-name alias: `import { Foo, type Bar,
            // Baz as BazAlias } from "...";`. The alias renders as `Original as Local`;
            // the `type` qualifier prefixes the whole token (`type Original as Local`)
            // when the name is type-only and the statement isn't already wholly
            // type-only.
            var typeOnlyNames = import.TypeOnlyNames;
            var aliases = import.Aliases;
            var parts = new string[import.Names.Length];
            for (var i = 0; i < import.Names.Length; i++)
            {
                var name = import.Names[i];
                var rendered =
                    aliases is not null && aliases.TryGetValue(name, out var local)
                        ? $"{name} as {local}"
                        : name;
                if (
                    typeOnlyNames is { Count: > 0 } qualified
                    && !import.TypeOnly
                    && qualified.Contains(name)
                )
                    rendered = "type " + rendered;
                parts[i] = rendered;
            }

            var extraParts = (import.ExtraAliases ?? []).Select(extra =>
                extra.TypeOnly && !import.TypeOnly
                    ? $"type {extra.Original} as {extra.Local}"
                    : $"{extra.Original} as {extra.Local}"
            );

            _sb.Write(string.Join(", ", parts.Concat(extraParts)));
            _sb.Write(" }");
        }
        _sb.Write(" from ");
        _sb.WriteQuoted(import.From);
        _sb.Write(";");
    }

    private void PrintReExport(TsReExport reExport)
    {
        _sb.Write("export ");
        if (reExport.TypeOnly)
            _sb.Write("type ");
        if (reExport.Names is ["*"])
        {
            _sb.Write("* from ");
        }
        else
        {
            _sb.Write("{ ");
            _sb.Write(string.Join(", ", reExport.Names));
            _sb.Write(" } from ");
        }

        _sb.WriteQuoted(reExport.From);
        _sb.Write(";");
    }

    private void PrintTypeAlias(TsTypeAlias alias)
    {
        PrintJSDoc(alias.JSDoc);
        if (alias.Exported)
            _sb.Write("export ");
        _sb.Write("type ");
        _sb.Write(alias.Name);
        PrintTypeParameters(alias.TypeParameters);
        _sb.Write(" = ");
        PrintType(alias.Type);
        _sb.Write(";");
    }

    private void PrintInterface(TsInterface iface)
    {
        if (iface.Exported)
            _sb.Write("export ");
        _sb.Write("interface ");
        _sb.Write(iface.Name);
        PrintTypeParameters(iface.TypeParameters);
        if (iface.Extends is { Count: > 0 } extends)
        {
            _sb.Write(" extends ");
            _sb.WriteList(extends, PrintType);
        }

        _sb.WriteBlock(() =>
        {
            foreach (var prop in iface.Properties)
            {
                PrintJSDoc(prop.JSDoc);
                PrintAccessibility(prop.Accessibility);
                if (prop.Readonly)
                    _sb.Write("readonly ");
                _sb.Write(prop.Name);
                if (prop.Optional)
                    _sb.Write("?");
                _sb.Write(": ");
                PrintType(prop.Type);
                _sb.Write(";");
                _sb.WriteLn();
            }

            if (iface.Methods is not null)
            {
                foreach (var method in iface.Methods)
                {
                    _sb.Write(method.Name);
                    PrintTypeParameters(method.TypeParameters);
                    _sb.Write("(");
                    PrintParameters(method.Parameters);
                    _sb.Write("): ");
                    PrintType(method.ReturnType);
                    _sb.Write(";");
                    _sb.WriteLn();
                }
            }
        });
    }

    /// <summary>
    /// Prints a declaration's JSDoc: one <c>/** text *&#47;</c> line per entry,
    /// ahead of the declaration it annotates.
    /// </summary>
    private void PrintJSDoc(TsJSDoc? jsdoc)
    {
        if (jsdoc is null)
            return;

        foreach (var line in jsdoc.Lines)
        {
            _sb.Write("/** ");
            _sb.Write(line);
            _sb.Write(" */");
            _sb.WriteLn();
        }
    }

    /// <summary>
    /// Prints a JSDoc inline, ahead of a parameter property on the same line:
    /// <c>/** text *&#47; </c> per entry.
    /// </summary>
    private void PrintInlineJSDoc(TsJSDoc? jsdoc)
    {
        if (jsdoc is null)
            return;

        foreach (var line in jsdoc.Lines)
        {
            _sb.Write("/** ");
            _sb.Write(line);
            _sb.Write(" */ ");
        }
    }

    private void PrintFunction(TsFunction func)
    {
        if (func.Overloads is { Count: > 0 } overloads)
            PrintFunctionOverloadSignatures(func, overloads);

        PrintJSDoc(func.JSDoc);

        if (func.Exported)
            _sb.Write("export ");
        if (func.Async)
            _sb.Write("async ");
        _sb.Write(func.Generator ? "function* " : "function ");
        _sb.Write(func.Name);
        PrintTypeParameters(func.TypeParameters);
        _sb.Write("(");
        PrintParameters(func.Parameters);
        if (func.ReturnType is { } returnType)
        {
            _sb.Write("): ");
            PrintType(returnType);
        }
        else
        {
            _sb.Write(")");
        }
        PrintBody(func.Body);
    }

    /// <summary>
    /// Prints <c>export function f(a: string): T;</c> per overload. Overload
    /// signatures never carry <c>async</c> / <c>*</c> — those modifiers belong
    /// to the implementation only.
    /// </summary>
    private void PrintFunctionOverloadSignatures(
        TsFunction func,
        IReadOnlyList<TsMethodOverload> overloads
    )
    {
        foreach (var overload in overloads)
        {
            PrintJSDoc(overload.JSDoc);
            if (func.Exported)
                _sb.Write("export ");
            _sb.Write("function ");
            PrintOverloadSignature(func.Name, overload);
        }
    }

    /// <summary>Prints <c>name&lt;T&gt;(params): ReturnType;</c> — the caller writes the prefix.</summary>
    private void PrintOverloadSignature(string name, TsMethodOverload overload)
    {
        _sb.Write(name);
        PrintTypeParameters(overload.TypeParameters);
        _sb.Write("(");
        PrintParameters(overload.Parameters);
        _sb.Write("): ");
        PrintType(overload.ReturnType);
        _sb.Write(";");
        _sb.WriteLn();
    }

    private void PrintNamespace(TsNamespaceDeclaration ns)
    {
        if (ns.Exported)
            _sb.Write("export ");

        if (ns.Declare)
            _sb.Write("declare ");

        _sb.Write("namespace ");
        _sb.Write(ns.Name);

        _sb.WriteBlock(() =>
        {
            // Functions (used by Branded companions)
            for (var i = 0; i < ns.Functions.Count; i++)
            {
                if (i > 0)
                    _sb.WriteLn();
                PrintFunction(ns.Functions[i]);
                _sb.WriteLn();
            }

            // Arbitrary nested members (used by nested types)
            if (ns.Members is not null)
            {
                var startIdx = ns.Functions.Count;
                for (var i = 0; i < ns.Members.Count; i++)
                {
                    if (startIdx + i > 0)
                        _sb.WriteLn();
                    PrintTopLevel(ns.Members[i]);
                    _sb.WriteLn();
                }
            }
        });
    }

    private void PrintConstObject(TsConstObject constObj)
    {
        if (constObj.Exported)
            _sb.Write("export ");
        _sb.Write("const ");
        _sb.Write(constObj.Name);

        if (constObj.Shape is { } shape)
        {
            _sb.Write(":");
            PrintObjectTypeBlock(shape);
        }

        _sb.Write(" =");

        _sb.WriteBlock(() =>
        {
            foreach (var entry in constObj.Entries)
            {
                PrintPropertyKey(entry.Key);
                _sb.Write(": ");
                PrintExpression(entry.Value);
                _sb.Write(",");
                _sb.WriteLn();
            }
        });

        _sb.Write(constObj.Shape is null ? " as const;" : ";");
    }

    /// <summary>
    /// An object type one field per line, each after its JSDoc, as the written shape
    /// of a const object.
    /// </summary>
    private void PrintObjectTypeBlock(TsObjectType shape)
    {
        _sb.WriteBlock(() =>
        {
            foreach (var field in shape.Fields)
            {
                PrintJSDoc(field.JSDoc);
                PrintObjectTypeField(field);
                _sb.Write(";");
                _sb.WriteLn();
            }
        });
    }

    private void PrintObjectTypeField(TsObjectTypeField field)
    {
        if (field.Readonly)
            _sb.Write("readonly ");

        _sb.Write(field.Name);
        _sb.Write(field.Optional ? "?: " : ": ");
        PrintType(field.Type);
    }

    /// <summary>
    /// The key of an object literal entry. A plain <c>__proto__:</c> key sets the
    /// object's prototype instead of defining a property, so it is printed computed.
    /// </summary>
    private void PrintPropertyKey(string key) =>
        _sb.Write(key == ProtoKey ? $"[\"{ProtoKey}\"]" : key);

    // ─── Class ──────────────────────────────────────────────

    private void PrintClass(TsClass tsClass)
    {
        if (tsClass.Exported)
            _sb.Write("export ");
        if (tsClass.Abstract)
            _sb.Write("abstract ");
        _sb.Write("class ");
        _sb.Write(tsClass.Name);
        PrintTypeParameters(tsClass.TypeParameters);

        if (tsClass.Extends is not null)
        {
            _sb.Write(" extends ");
            PrintType(tsClass.Extends);
        }

        if (tsClass.Implements is { Count: > 0 })
        {
            _sb.Write(" implements ");
            _sb.WriteList(tsClass.Implements, PrintType);
        }

        // Group members in idiomatic TS order: fields → constructor → getters/setters → methods
        var fields = tsClass.Members.Where(m => m is TsFieldMember).ToList();
        var accessors = tsClass.Members.Where(m => m is TsGetterMember or TsSetterMember).ToList();
        var methods = tsClass.Members.Where(m => m is TsMethodMember).ToList();

        var others = tsClass
            .Members.Where(m =>
                m is not (TsFieldMember or TsGetterMember or TsSetterMember or TsMethodMember)
            )
            .ToList();

        var fieldIsMultiLine = fields.Select(PrintsOnMultipleLines).ToArray();

        _sb.WriteBlock(() =>
        {
            var firstGroup = true;

            void PrintGroup(
                IReadOnlyList<TsClassMember> members,
                Func<int, bool> needsBlankLineBefore
            )
            {
                if (members.Count == 0)
                    return;

                if (!firstGroup)
                    _sb.WriteLn();

                firstGroup = false;

                for (var i = 0; i < members.Count; i++)
                {
                    if (i > 0 && needsBlankLineBefore(i))
                        _sb.WriteLn();

                    PrintClassMember(members[i]);
                }
            }

            void PrintSeparatedGroup(IReadOnlyList<TsClassMember> members) =>
                PrintGroup(members, static _ => true);

            // Fields first
            PrintGroup(fields, i => FieldsNeedBlankLineBetween(fieldIsMultiLine, i));

            // Constructor
            if (tsClass.Constructor is not null)
            {
                if (!firstGroup)
                    _sb.WriteLn();
                firstGroup = false;
                PrintConstructor(tsClass.Constructor);
            }

            // Getters / setters
            PrintSeparatedGroup(accessors);

            // Methods
            PrintSeparatedGroup(methods);

            // Anything else
            PrintSeparatedGroup(others);
        });
    }

    /// <summary>
    /// The field-block blank-line rule (issue #682): consecutive single-line
    /// fields print one after another, and a field that prints on more than
    /// one line gets a blank line above and below. The rule is symmetric — if
    /// either side is multi-line, the pair is separated. <paramref name="fieldIsMultiLine"/>
    /// holds each field's <see cref="PrintsOnMultipleLines"/>, measured once per class.
    /// </summary>
    private static bool FieldsNeedBlankLineBetween(
        IReadOnlyList<bool> fieldIsMultiLine,
        int nextIndex
    ) => fieldIsMultiLine[nextIndex - 1] || fieldIsMultiLine[nextIndex];

    /// <summary>
    /// Whether a class member prints on more than one line. Measured by
    /// rendering the member with a scratch printer, so the spacing rule
    /// cannot drift from the real layout: a multi-line initializer (object
    /// literal, arrow function, raw text with newlines) counts, and so does
    /// a doc comment, which prints on its own line(s) above the declaration.
    /// Anything else printed on its own line above a member (for example a
    /// decorator, should the AST grow one) counts the same way.
    /// </summary>
    private static bool PrintsOnMultipleLines(TsClassMember member)
    {
        var scratch = new Printer();
        scratch.PrintClassMember(member);

        return scratch._sb.GetRawString().TrimEnd('\n').Contains('\n');
    }

    private void PrintConstructor(TsConstructor ctor)
    {
        if (ctor.Overloads is { Count: > 0 })
        {
            // Print overload signatures first
            foreach (var overload in ctor.Overloads)
            {
                PrintJSDoc(overload.JSDoc);
                PrintConstructorHead(ctor.Accessibility, overload.Parameters);
                _sb.Write(";");
                _sb.WriteLn();
            }

            // Print dispatcher implementation — untagged: each signature
            // carries its own declaration's JSDoc.
            PrintConstructorHead(ctor.Accessibility, ctor.Parameters);
            PrintBody(ctor.Body);
        }
        else
        {
            // Single constructor (no overloads)
            PrintConstructorHead(ctor.Accessibility, ctor.Parameters);

            if (ctor.Body.Count == 0)
            {
                _sb.WriteEmptyBlock();
            }
            else
            {
                PrintBody(ctor.Body);
            }
        }

        _sb.WriteLn();
    }

    private void PrintConstructorHead(
        TsAccessibility accessibility,
        IReadOnlyList<TsConstructorParam> parameters
    )
    {
        PrintAccessibility(accessibility);
        _sb.Write("constructor(");
        _sb.WriteList(parameters, PrintConstructorParam);
        _sb.Write(")");
    }

    private void PrintConstructorParam(TsConstructorParam p)
    {
        PrintInlineJSDoc(p.JSDoc);

        // In TS, constructor params without any modifier (public/readonly/etc) don't create properties.
        // Emit explicit "public" when not readonly but the param should still be a property.
        // None = plain parameter, no property created (used for exception constructors).
        // Rest parameters (`...args`) cannot carry parameter-property modifiers
        // — TS rejects `public ...args` / `readonly ...args` as a syntax
        // error. Skip the modifier prefix for those defensively in case the
        // bridge wired one up.
        if (p.Rest || p.Accessibility == TsAccessibility.None)
        { /* no modifier — plain parameter */
        }
        else if (p.Accessibility == TsAccessibility.Public && !p.Readonly)
            _sb.Write("public ");
        else
            PrintAccessibility(p.Accessibility);
        if (p.Readonly && !p.Rest)
            _sb.Write("readonly ");
        if (p.Rest)
            _sb.Write("...");
        _sb.Write(p.Name);
        if (p.Optional && p.DefaultValue is null)
            _sb.Write("?");
        _sb.Write(": ");
        PrintType(p.Type);
        if (p.DefaultValue is not null)
        {
            _sb.Write(" = ");
            PrintExpression(p.DefaultValue);
        }
    }

    private void PrintClassMember(TsClassMember member)
    {
        switch (member)
        {
            case TsGetterMember getter:
                PrintJSDoc(getter.JSDoc);
                PrintAccessibility(getter.Accessibility);

                if (getter.Static)
                    _sb.Write("static ");
                if (getter.Override)
                    _sb.Write("override ");
                _sb.Write("get ");
                _sb.Write(getter.Name);
                _sb.Write("(): ");
                PrintType(getter.ReturnType);
                PrintBody(getter.Body);
                _sb.WriteLn();
                break;

            case TsSetterMember setter:
                PrintJSDoc(setter.JSDoc);
                PrintAccessibility(setter.Accessibility);

                if (setter.Static)
                    _sb.Write("static ");
                _sb.Write("set ");
                _sb.Write(setter.Name);
                _sb.Write("(");
                _sb.Write(setter.ValueParam.Name);
                _sb.Write(": ");
                PrintType(setter.ValueParam.Type!);
                _sb.Write(")");
                PrintBody(setter.Body);
                _sb.WriteLn();
                break;

            case TsFieldMember field:
                PrintJSDoc(field.JSDoc);

                if (field.Declare)
                    _sb.Write("declare ");

                PrintAccessibility(field.Accessibility);

                if (field.Abstract)
                    _sb.Write("abstract ");

                if (field.Static)
                    _sb.Write("static ");

                if (field.Override)
                    _sb.Write("override ");

                if (field.Readonly)
                    _sb.Write("readonly ");
                _sb.Write(field.Name);
                if (field.Optional)
                    _sb.Write("?");
                else if (field.DefinitelyAssigned && field.Initializer is null)
                    _sb.Write("!");

                if (field.Static && IsPureInitializer(field.Initializer))
                    PrintPureStaticFieldInitializer(field.Initializer!, field.Type);
                else
                    PrintFieldTypeAndInitializer(field);

                _sb.Write(";");
                _sb.WriteLn();
                break;

            case TsMethodMember method:
                if (method.Overloads is { Count: > 0 })
                {
                    // Print overload signatures first, each carrying its own
                    // declaration's JSDoc; the implementation carries none.
                    foreach (var overload in method.Overloads)
                    {
                        PrintJSDoc(overload.JSDoc);
                        PrintAccessibility(method.Accessibility);
                        if (method.Static)
                            _sb.Write("static ");
                        if (method.Override)
                            _sb.Write("override ");
                        PrintOverloadSignature(method.Name, overload);
                    }
                }
                else
                {
                    PrintJSDoc(method.JSDoc);
                }

                // Print the implementation (or single method).
                // Abstract methods carry no body — emit the signature
                // followed by `;` and skip the block.
                PrintAccessibility(method.Accessibility);
                if (method.Abstract)
                    _sb.Write("abstract ");
                if (method.Static)
                    _sb.Write("static ");
                if (method.Override)
                    _sb.Write("override ");
                if (method.Async)
                    _sb.Write("async ");
                if (method.Generator)
                    _sb.Write("*");
                PrintMethodKey(method);
                PrintTypeParameters(method.TypeParameters);
                _sb.Write("(");
                PrintParameters(method.Parameters);
                _sb.Write("): ");
                PrintType(method.ReturnType);
                if (method.Abstract)
                {
                    _sb.Write(";");
                    _sb.WriteLn();
                }
                else
                {
                    PrintBody(method.Body);
                    _sb.WriteLn();
                }
                break;
        }
    }

    private void PrintMethodKey(TsMethodMember method)
    {
        if (method.ComputedKey is null)
        {
            _sb.Write(method.Name);
            return;
        }

        _sb.Write("[");
        PrintExpression(method.ComputedKey);
        _sb.Write("]");
    }

    // ─── Types ──────────────────────────────────────────────

    private void PrintType(TsType type)
    {
        switch (type)
        {
            case TsNumberType:
                _sb.Write("number");
                break;
            case TsStringType:
                _sb.Write("string");
                break;
            case TsBooleanType:
                _sb.Write("boolean");
                break;
            case TsVoidType:
                _sb.Write("void");
                break;
            case TsBigIntType:
                _sb.Write("bigint");
                break;
            case TsAnyType:
                _sb.Write("any");
                break;

            case TsUniqueSymbolType:
                _sb.Write("unique symbol");
                break;

            case TsTypeQuery query:
                _sb.Write("typeof ");
                _sb.Write(query.EntityName);
                break;

            case TsNamedType named when named.RuntimeHelperBinding is { } binding:
                // A class-shaped runtime helper used as a type (HashSet<T>,
                // UUID, Grouping<K, V>): the root binding prints deferred so
                // it agrees with the aliased import; the rest of a dotted
                // name (Temporal.PlainDate) stays compiler-authored.
                var typeRest = named.Name[binding.Length..];
                _sb.Write(HelperName(binding) + typeRest);
                if (named.TypeArguments is { Count: > 0 })
                {
                    _sb.Write("<");
                    _sb.WriteList(named.TypeArguments, PrintType);
                    _sb.Write(">");
                }

                break;

            case TsNamedType named:
                _sb.Write(named.Name);
                if (named.TypeArguments is { Count: > 0 })
                {
                    _sb.Write("<");
                    _sb.WriteList(named.TypeArguments, PrintType);
                    _sb.Write(">");
                }

                break;

            case TsArrayType array:
                if (array.IsReadonly)
                    _sb.Write("readonly ");

                PrintArrayElementType(array.ElementType);
                _sb.Write("[]");
                break;

            case TsPromiseType promise:
                _sb.Write("Promise<");
                PrintType(promise.Inner);
                _sb.Write(">");
                break;

            case TsStringLiteralType lit:
                _sb.WriteQuoted(lit.Value);
                break;

            case TsUnionType union:
                _sb.WriteList(union.Types, PrintTypeInUnion, " | ");
                break;

            case TsIntersectionType intersection:
                _sb.WriteList(intersection.Types, PrintTypeInUnion, " & ");
                break;

            case TsTupleType tuple:
                PrintTupleType(tuple);
                break;

            case TsBooleanLiteralType booleanLiteral:
                _sb.Write(booleanLiteral.Value ? "true" : "false");
                break;

            case TsTypePredicateType predicate:
                if (predicate.IsAsserts)
                    _sb.Write("asserts ");
                _sb.Write(predicate.ParameterName);
                _sb.Write(" is ");
                PrintType(predicate.Type);
                break;

            case TsFunctionType funcType:
                _sb.Write("(");
                if (funcType.ThisType is { } thisType)
                {
                    _sb.Write("this: ");
                    PrintType(thisType);
                    if (funcType.Parameters.Count > 0)
                        _sb.Write(", ");
                }
                PrintParameters(funcType.Parameters);
                _sb.Write(") => ");
                PrintType(funcType.ReturnType);
                break;

            case TsObjectType obj:
                if (obj.Fields.Count == 0)
                {
                    _sb.Write("{}");
                    break;
                }

                _sb.Write("{ ");

                for (var i = 0; i < obj.Fields.Count; i++)
                {
                    if (i > 0)
                        _sb.Write("; ");

                    PrintInlineJSDoc(obj.Fields[i].JSDoc);
                    PrintObjectTypeField(obj.Fields[i]);
                }

                _sb.Write(" }");
                break;
        }
    }

    /// <summary>
    /// <c>[T1, T2]</c>, or <c>readonly [label: T1, …]</c> when the tuple is
    /// readonly or labeled.
    /// </summary>
    private void PrintTupleType(TsTupleType tuple)
    {
        if (tuple.IsReadonly)
            _sb.Write("readonly ");

        _sb.Write("[");

        for (var i = 0; i < tuple.Elements.Count; i++)
        {
            if (i > 0)
                _sb.Write(", ");

            if (tuple.Labels is { } labels)
                _sb.Write($"{labels[i]}: ");

            PrintType(tuple.Elements[i]);
        }

        _sb.Write("]");
    }

    /// <summary>
    /// Wraps low-precedence types (function types) in parentheses when they
    /// appear as operands of a union or intersection. Without this,
    /// <c>(args) =&gt; R | null</c> parses as a function returning <c>R | null</c>
    /// instead of a nullable function <c>((args) =&gt; R) | null</c>.
    /// </summary>
    private void PrintTypeInUnion(TsType type)
    {
        if (type is TsFunctionType)
        {
            _sb.Write("(");
            PrintType(type);
            _sb.Write(")");
        }
        else
        {
            PrintType(type);
        }
    }

    /// <summary>
    /// Wraps an array element type that binds looser than the <c>[]</c>
    /// suffix in parentheses. Without this, a nullable element prints as
    /// <c>T | null[]</c>, which TypeScript parses as <c>T | (null[])</c>
    /// instead of <c>(T | null)[]</c>; a function element would likewise
    /// read as a function returning an array.
    /// </summary>
    private void PrintArrayElementType(TsType elementType)
    {
        if (
            elementType
            is TsUnionType
                or TsIntersectionType
                or TsFunctionType
                or TsArrayType { IsReadonly: true }
                or TsTupleType { IsReadonly: true }
        )
        {
            _sb.Write("(");
            PrintType(elementType);
            _sb.Write(")");
        }
        else
        {
            PrintType(elementType);
        }
    }

    // ─── Statements ─────────────────────────────────────────

    private void PrintStatement(TsStatement stmt)
    {
        switch (stmt)
        {
            case TsReturnStatement ret:
                _sb.Write("return");
                if (ret.Expression is not null)
                {
                    _sb.Write(" ");
                    PrintExpression(ret.Expression);
                }

                _sb.Write(";");
                break;

            case TsIfStatement ifStmt:
                _sb.Write("if (");
                PrintExpression(ifStmt.Condition);
                _sb.Write(")");
                _sb.WriteBlock(() => PrintStatementList(ifStmt.Then));
                if (ifStmt.Else is { Count: > 0 })
                {
                    _sb.Write(" else");
                    _sb.WriteBlock(() => PrintStatementList(ifStmt.Else));
                }

                break;

            case TsBlockStatement block:
                _sb.Write("{");
                _sb.WriteLn();
                _sb.Indent();
                PrintStatementList(block.Statements);
                _sb.Dedent();
                _sb.Write("}");
                break;

            case TsThrowStatement throwStmt:
                _sb.Write("throw ");
                PrintExpression(throwStmt.Expression);
                _sb.Write(";");
                break;

            case TsInlineCodeStatement inlineCode:
                PrintInlineCodeParts(inlineCode.Parts);
                break;

            case TsTryStatement tryStmt:
                PrintTry(tryStmt);
                break;

            case TsRawStatement raw:
                // Multi-line raw text needs explicit re-indent on every line:
                // the underlying builder only applies the indent prefix at the
                // start of a line, and a `\n` inside `raw.Text` would emit the
                // following content at column 0 otherwise. Split on `\n`,
                // then emit each line with `Write` (which re-indents) and a
                // `WriteLn` between lines. The trailing newline still comes
                // from `PrintStatementList`, so we don't emit one ourselves
                // after the last line.
                var rawLines = raw.Text.Split('\n');
                for (var i = 0; i < rawLines.Length; i++)
                {
                    if (i > 0)
                        _sb.WriteLn();

                    // A blank line stays empty instead of carrying the indent.
                    if (rawLines[i].Length > 0)
                        _sb.Write(rawLines[i]);
                }
                break;

            case TsVariableDeclaration varDecl:
                PrintJSDoc(varDecl.JSDoc);
                if (varDecl.Exported)
                    _sb.Write("export ");
                _sb.Write(varDecl.Const ? "const " : "let ");
                _sb.Write(varDecl.Name);

                if (varDecl.DefinitelyAssigned)
                    _sb.Write("!");

                if (varDecl.Type is not null)
                {
                    _sb.Write(": ");
                    PrintType(varDecl.Type);
                }

                if (varDecl.Initializer is not null)
                {
                    _sb.Write(" = ");

                    if (varDecl.InitializesStaticState && IsPureInitializer(varDecl.Initializer))
                        _sb.Write(PureAnnotation);

                    PrintExpression(varDecl.Initializer);
                }

                _sb.Write(";");
                break;

            case TsDestructuringDeclaration destructuring:
                if (destructuring.Exported)
                    _sb.Write("export ");
                _sb.Write(destructuring.Const ? "const " : "let ");
                _sb.Write("[");
                for (var i = 0; i < destructuring.Names.Count; i++)
                {
                    if (i > 0)
                        _sb.Write(", ");
                    // A null name is a discard: emit an empty slot. When the
                    // discard is trailing the comma above already produced the
                    // hole; for a leading/middle discard the empty slot is the
                    // absence of a name between commas.
                    if (destructuring.Names[i] is { } name)
                        _sb.Write(name);
                }
                _sb.Write("] = ");
                PrintExpression(destructuring.Initializer);
                _sb.Write(";");
                break;

            case TsExpressionStatement exprStmt:
                PrintOperand(exprStmt.Expression, TsOperandSlot.ExpressionStatement);
                _sb.Write(";");
                break;

            case TsYieldStatement yieldStmt:
                _sb.Write("yield ");
                PrintExpression(yieldStmt.Expression);
                _sb.Write(";");
                break;

            case TsYieldBreakStatement:
                _sb.Write("return;");
                break;

            case TsSwitchStatement switchStmt:
                _sb.Write("switch (");
                PrintExpression(switchStmt.Discriminant);
                _sb.Write(")");
                _sb.WriteBlock(() =>
                {
                    foreach (var c in switchStmt.Cases)
                    {
                        if (c.Test is not null)
                        {
                            _sb.Write("case ");
                            PrintExpression(c.Test);
                            _sb.Write(":");
                        }
                        else
                        {
                            _sb.Write("default:");
                        }

                        _sb.WriteLn();
                        _sb.Indent();
                        PrintStatementList(c.Body);
                        _sb.Dedent();
                    }
                });
                break;
        }
    }

    // ─── Expressions ────────────────────────────────────────

    /// <summary>
    /// Prints <paramref name="operand"/> in <paramref name="slot"/>, wrapping
    /// it in parentheses when printing it bare would regroup it (or be
    /// rejected by the grammar). The IR carries no source parentheses, so
    /// this is where the tree's grouping is turned back into text.
    /// </summary>
    private void PrintOperand(TsExpression operand, TsOperandSlot slot) =>
        PrintGrouped(operand, TsParenthesization.NeedsParens(operand, slot));

    /// <summary>
    /// An argument, array element or object value: any assignment-level
    /// expression prints bare.
    /// </summary>
    private void PrintListElement(TsExpression element) =>
        PrintOperand(element, TsOperandSlot.AssignmentExpression);

    private void PrintGrouped(TsExpression expression, bool parenthesized)
    {
        if (parenthesized)
            PrintParenthesized(expression);
        else
            PrintExpression(expression);
    }

    /// <summary>
    /// <c>a?.b</c> (and a step marked as continuing the chain, the <c>.c</c>
    /// of <c>a?.b.c</c>) extends an optional chain on purpose; any other
    /// plain <c>.b</c> on an optional-chain object must close the chain first.
    /// </summary>
    private static TsOperandSlot AccessObjectSlot(bool optional, bool continuesChain) =>
        optional || continuesChain ? TsOperandSlot.AccessTarget : TsOperandSlot.MemberObject;

    /// <summary>
    /// Inline TypeScript: the text verbatim (a line break in it is kept as is, so a
    /// template literal in the text keeps its content), each hole in its operand slot.
    /// </summary>
    private void PrintInlineCodeParts(IReadOnlyList<TsInlineCodePart> parts)
    {
        foreach (var part in parts)
        {
            switch (part)
            {
                case TsInlineCodeText text:
                    _sb.Write(text.Text);
                    break;
                case TsInlineCodeHole hole:
                    PrintOperand(hole.Expression, TsOperandSlot.InlineCodeHole);
                    break;
                case TsInlineCodeLineBreak:
                    _sb.WriteLn();
                    break;
            }
        }
    }

    private void PrintParenthesized(TsExpression expression)
    {
        _sb.Write("(");
        PrintExpression(expression);
        _sb.Write(")");
    }

    private void PrintExpression(TsExpression expr)
    {
        switch (expr)
        {
            case TsIdentifier id:
                _sb.Write(id.Name);
                break;

            case TsTypeReference typeRef:
                // Same on-the-wire syntax as a bare identifier; the wrapper exists
                // purely so the import collector can see the cross-package origin.
                _sb.Write(typeRef.Name);
                break;

            case TsModuleExportReference exportReference:
                // Likewise: the owner type only tells the import collector where
                // the name is exported from.
                _sb.Write(exportReference.Name);
                break;

            case TsRuntimeHelperReference helper:
                _sb.Write(HelperName(helper.Name));
                break;

            case TsLiteral lit:
                _sb.Write(lit.Raw);
                break;

            case TsTemplate template:
                PrintTemplate(template);
                break;

            case TsStringLiteral str:
                _sb.WriteQuoted(str.Value);
                break;

            case TsTemplateLiteral tmpl:
                _sb.Write("`");
                for (var i = 0; i < tmpl.Quasis.Count; i++)
                {
                    _sb.WriteTemplateText(tmpl.Quasis[i]);
                    if (i < tmpl.Expressions.Count)
                    {
                        _sb.Write("${");
                        PrintExpression(tmpl.Expressions[i]);
                        _sb.Write("}");
                    }
                }

                _sb.Write("`");
                break;

            case TsBinaryExpression bin:
                PrintOperand(bin.Left, TsOperandSlot.BinaryLeft(bin.Operator));
                _sb.Write(bin.Operator == "," ? ", " : $" {bin.Operator} ");
                PrintOperand(bin.Right, TsOperandSlot.BinaryRight(bin.Operator));
                break;

            case TsPropertyAccess access:
                PrintOperand(
                    access.Object,
                    AccessObjectSlot(access.Optional, access.ContinuesChain)
                );
                _sb.Write(access.Optional ? "?." : ".");
                _sb.Write(access.Property);
                break;

            case TsCallExpression call:
                PrintOperand(call.Callee, TsOperandSlot.AccessTarget);
                if (call.Optional)
                    _sb.Write("?.");
                if (call.TypeArguments is { Count: > 0 } typeArgs)
                {
                    _sb.Write("<");
                    _sb.WriteList(typeArgs, PrintType);
                    _sb.Write(">");
                }
                _sb.Write("(");
                _sb.WriteList(call.Arguments, PrintListElement);
                _sb.Write(")");
                break;

            case TsLinqCall linq:
                _sb.Write(HelperName("linq"));
                _sb.Write("(");
                _sb.WriteList(linq.Args, PrintListElement);
                _sb.Write(")");
                break;

            case TsObjectLiteral obj:
                PrintObjectLiteral(obj);
                break;

            case TsSpreadExpression spread:
                _sb.Write("...");
                PrintOperand(spread.Expression, TsOperandSlot.AssignmentExpression);
                break;

            case TsElementAccess elemAccess:
                PrintOperand(
                    elemAccess.Object,
                    AccessObjectSlot(elemAccess.Optional, elemAccess.ContinuesChain)
                );
                _sb.Write(elemAccess.Optional ? "?.[" : "[");
                PrintExpression(elemAccess.Index);
                _sb.Write("]");
                break;

            case TsArrayHole:
                break;

            case TsArrayLiteral arrayLit:
                if (ShouldPrintArrayMultiLine(arrayLit))
                {
                    _sb.Write("[");
                    _sb.WriteLn();
                    _sb.Indent();
                    foreach (var elem in arrayLit.Elements)
                    {
                        PrintListElement(elem);
                        _sb.Write(",");
                        _sb.WriteLn();
                    }
                    _sb.Dedent();
                    _sb.Write("]");
                }
                else
                {
                    _sb.Write("[");
                    _sb.WriteList(arrayLit.Elements, PrintListElement);
                    _sb.Write("]");
                }
                break;

            case TsNewExpression newExpr:
                _sb.Write("new ");
                PrintOperand(newExpr.Callee, TsOperandSlot.NewCallee);

                if (newExpr.TypeArguments is { Count: > 0 } newTypeArgs)
                {
                    _sb.Write("<");
                    _sb.WriteList(newTypeArgs, PrintType);
                    _sb.Write(">");
                }

                _sb.Write("(");
                _sb.WriteList(newExpr.Arguments, PrintListElement);
                _sb.Write(")");
                break;

            case TsAwaitExpression awaitExpr:
                _sb.Write("await ");
                PrintOperand(awaitExpr.Expression, TsOperandSlot.Await);
                break;

            case TsArrowFunction arrow:
                if (arrow.Async)
                    _sb.Write("async ");
                _sb.Write("(");
                PrintParameters(arrow.Parameters);
                // Write the arrow without a trailing space — the concise form
                // adds its own space before the expression, and the block form
                // lets WriteBlock insert " {" (including its own leading space).
                // Without this split, block-body arrows printed as `) =>  {`
                // with two spaces — cosmetic but visible in every route handler
                // in sample-todo-service/src/program.ts.
                _sb.Write(")");

                if (arrow.ReturnType is not null)
                {
                    _sb.Write(": ");
                    PrintType(arrow.ReturnType);
                }

                _sb.Write(" =>");
                if (arrow.Body is [TsReturnStatement { Expression: { } returnExpr }])
                {
                    _sb.Write(" ");
                    PrintOperand(returnExpr, TsOperandSlot.ConciseArrowBody);
                }
                else
                {
                    _sb.WriteBlock(() => PrintStatementList(arrow.Body));
                }
                break;

            case TsNamedFunctionExpression named:
                if (named.Async)
                    _sb.Write("async ");
                _sb.Write("function ");
                _sb.Write(named.Name);
                _sb.Write("(");
                PrintParameters(named.Parameters);
                _sb.Write(")");
                if (named.ReturnType is not null)
                {
                    _sb.Write(": ");
                    PrintType(named.ReturnType);
                }
                _sb.WriteBlock(() => PrintStatementList(named.Body));
                break;

            case TsUnaryExpression unary:
                _sb.Write(unary.Operator);
                PrintOperand(unary.Operand, TsOperandSlot.Prefix(unary.Operator));
                break;

            case TsPostfixUnaryExpression postfix:
                PrintOperand(postfix.Operand, TsOperandSlot.Postfix);
                _sb.Write(postfix.Operator);
                break;

            case TsParenthesized paren:
                PrintParenthesized(paren.Expression);
                break;

            case TsInlineCode inlineCode:
                _sb.Write("(");
                PrintInlineCodeParts(inlineCode.Parts);
                _sb.Write(")");
                break;

            case TsCastExpression cast:
                PrintOperand(cast.Expression, TsOperandSlot.CastOperand);
                _sb.Write(" as ");
                PrintType(cast.Type);
                break;

            case TsConditionalExpression cond:
                PrintOperand(cond.Condition, TsOperandSlot.ConditionalTest);
                _sb.Write(" ? ");
                PrintOperand(cond.WhenTrue, TsOperandSlot.AssignmentExpression);
                _sb.Write(" : ");
                PrintOperand(cond.WhenFalse, TsOperandSlot.AssignmentExpression);
                break;

            case TsJsxElement jsx:
                PrintJsxElement(jsx);
                break;
        }
    }

    /// <summary>
    /// Emits a JSX element: <c>&lt;tag attr="lit" attr2={expr}&gt;children&lt;/tag&gt;</c>,
    /// or the self-closing form <c>&lt;tag ... /&gt;</c> when the element is
    /// flagged self-closing or has no children.
    /// </summary>
    private void PrintJsxElement(TsJsxElement jsx)
    {
        _sb.Write("<");
        _sb.Write(jsx.TagName);
        foreach (var attribute in jsx.Attributes)
        {
            _sb.Write(" ");
            PrintJsxAttribute(attribute);
        }

        if (jsx.SelfClosing || jsx.Children.Count == 0)
        {
            _sb.Write(" />");
            return;
        }

        _sb.Write(">");
        foreach (var child in jsx.Children)
            PrintJsxChild(child);
        _sb.Write("</");
        _sb.Write(jsx.TagName);
        _sb.Write(">");
    }

    /// <summary>
    /// Emits a single JSX attribute: <c>name="..."</c> for a string value,
    /// <c>name={...}</c> for an embedded expression.
    /// </summary>
    private void PrintJsxAttribute(TsJsxAttribute attribute)
    {
        _sb.Write(attribute.Name);

        switch (attribute.Value)
        {
            case TsJsxAttributeStringValue str when HasLoneSurrogate(str.Value):
                // A JSX attribute string has no escapes: the value becomes a JS string.
                _sb.Write("={");
                _sb.WriteQuoted(str.Value);
                _sb.Write("}");
                break;
            case TsJsxAttributeStringValue str:
                _sb.Write("=\"");
                _sb.Write(EscapeJsxAttribute(str.Value));
                _sb.Write("\"");
                break;
            case TsJsxAttributeExpressionValue expr:
                _sb.Write("={");
                PrintExpression(expr.Expression);
                _sb.Write("}");
                break;
        }
    }

    /// <summary>
    /// Emits a JSX child: raw text, an embedded expression (<c>{expr}</c>), or
    /// a nested element.
    /// </summary>
    private void PrintJsxChild(TsJsxChild child)
    {
        switch (child)
        {
            case TsJsxText text:
                PrintJsxText(text.Value);
                break;
            case TsJsxExpressionChild expr:
                _sb.Write("{");
                PrintExpression(expr.Expression);
                _sb.Write("}");
                break;
            case TsJsxElementChild element:
                PrintJsxElement(element.Element);
                break;
        }
    }

    /// <summary>
    /// Prints JSX text. An unpaired surrogate cannot be written as a character
    /// (the file would not be valid UTF-8) nor as an entity, so it becomes a
    /// string expression, <c>{"\ud83d"}</c> (FR-073).
    /// </summary>
    private void PrintJsxText(string value)
    {
        var start = 0;

        for (var index = 0; index < value.Length; index++)
        {
            if (!SurrogateText.IsLone(value, index))
                continue;

            _sb.Write(EscapeJsxText(value[start..index]));
            _sb.Write("{");
            _sb.WriteQuoted(value[index].ToString());
            _sb.Write("}");
            start = index + 1;
        }

        _sb.Write(EscapeJsxText(value[start..]));
    }

    private static bool HasLoneSurrogate(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (SurrogateText.IsLone(value, index))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Escapes literal JSX text so metacharacters survive the TSX parser. A bare
    /// <c>&lt;</c> opens a child tag and <c>{</c> opens an expression container —
    /// both hard parse errors — so they, plus <c>&amp;</c> (which would otherwise
    /// start an HTML entity), are emitted as character references. <c>&gt;</c> and
    /// <c>}</c> (legal as bare text) are escaped defensively to avoid ambiguity with
    /// adjacent tag/expression delimiters.
    /// </summary>
    private static string EscapeJsxText(string value) =>
        value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("{", "&#123;")
            .Replace("}", "&#125;");

    /// <summary>
    /// Escapes a double-quoted JSX attribute value. A JSX attribute string is NOT a
    /// JS string literal (a backslash is literal, not an escape), so the delimiting
    /// quote and an entity-starting <c>&amp;</c> are emitted as character references
    /// rather than backslash escapes; a stray <c>&lt;</c> is escaped for HTML safety.
    /// </summary>
    private static string EscapeJsxAttribute(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace("\"", "&quot;");

    /// <summary>
    /// Renders a <see cref="TsTemplate"/> by walking the template string and emitting
    /// each chunk in turn: literal text is written verbatim, while
    /// <c>$this</c>/<c>$N</c>/<c>$TN</c> placeholders trigger a substitution. Value
    /// placeholders (<c>$this</c>, <c>$N</c>) recurse via <see cref="PrintExpression"/>
    /// so complex argument shapes (nested calls, lambdas, binary operators) round-trip
    /// correctly, parenthesized when the placeholder's neighbor tokens would regroup
    /// them (see <see cref="TsTemplateShape.SlotsAt"/>). Type placeholders (<c>$TN</c>)
    /// are plain identifier names and emit as raw text.
    /// </summary>
    private void PrintTemplate(TsTemplate template)
    {
        var text = template.Template;
        var shape = TsTemplateShape.Of(template);
        var i = 0;

        while (i < text.Length)
        {
            // Look for the next placeholder; everything before it is literal text.
            var dollar = text.IndexOf('$', i);

            if (dollar < 0)
            {
                WriteTemplateLiteral(text[i..], template.RuntimeImports);
                break;
            }

            if (dollar > i)
                WriteTemplateLiteral(text[i..dollar], template.RuntimeImports);

            i = PrintPlaceholderAt(template, shape, dollar);
        }
    }

    /// <summary>
    /// Writes a template's literal chunk with the helper names it declares in
    /// <c>RuntimeImports</c> deferred (<see cref="DeferredHelperNames"/>) so
    /// the file-level pass resolves each to the alias or the plain name. Only
    /// declared names are touched — the rest of the chunk is compiler-authored
    /// text — and the boundaries never accept a name arriving through a
    /// property access, so an <c>a.intDiv</c> member never rewrites.
    /// </summary>
    private void WriteTemplateLiteral(string chunk, IReadOnlyList<string> runtimeImports)
    {
        var deferred = runtimeImports.Aggregate(chunk, (current, helper) =>
            Regex.Replace(current, $@"(?<![\w$.]){Regex.Escape(helper)}(?![\w$])", _ =>
                DeferredHelperNames.Wrap(helper)
            )
        );

        _sb.Write(deferred);
    }

    /// <summary>
    /// Writes the placeholder at <paramref name="dollar"/> and returns the
    /// offset after it. A lone <c>$</c>, an out-of-range <c>$N</c>/<c>$TN</c>
    /// or an unrecognized placeholder emits the <c>$</c> verbatim and
    /// advances one character.
    /// </summary>
    private int PrintPlaceholderAt(TsTemplate template, TsTemplateShape shape, int dollar)
    {
        var placeholder = TsTemplatePlaceholder.Match(template.Template, dollar);

        switch (placeholder)
        {
            case { Kind: TsTemplatePlaceholderKind.Receiver } receiver:
                if (template.Receiver is not null)
                    PrintTemplateValue(template.Receiver, shape.SlotsAt(dollar));

                return dollar + receiver.Length;

            case { Kind: TsTemplatePlaceholderKind.TypeArgument } typeArgument
                when typeArgument.Index < template.TypeArguments.Count:
                PrintType(template.TypeArguments[typeArgument.Index]);

                return dollar + typeArgument.Length;

            case { Kind: TsTemplatePlaceholderKind.TypeArgument } typeArgument
                when typeArgument.Index < template.TypeArgumentNames.Count:
                _sb.Write(template.TypeArgumentNames[typeArgument.Index]);

                return dollar + typeArgument.Length;

            case { } argument when argument.Resolve(template) is { } value:
                PrintTemplateValue(value, shape.SlotsAt(dollar));

                return dollar + argument.Length;

            default:
                _sb.Write("$");

                return dollar + 1;
        }
    }

    /// <summary>
    /// Substitutes a value placeholder, parenthesized when either neighbor
    /// token of the placeholder would regroup the substituted node.
    /// </summary>
    private void PrintTemplateValue(TsExpression value, IReadOnlyList<TsOperandSlot> slots) =>
        PrintGrouped(value, TsParenthesization.NeedsParensInAny(value, slots));

    private void PrintObjectLiteral(TsObjectLiteral obj)
    {
        if (obj.Properties.Count == 0)
        {
            _sb.Write("{}");
            return;
        }

        if (!ShouldPrintObjectMultiLine(obj))
        {
            _sb.Write("{ ");
            _sb.WriteList(obj.Properties, PrintObjectProperty);
            _sb.Write(" }");
        }
        else
        {
            _sb.Write("{");
            _sb.WriteLn();
            _sb.Indent();
            foreach (var prop in obj.Properties)
            {
                PrintObjectProperty(prop);
                _sb.Write(",");
                _sb.WriteLn();
            }

            _sb.Dedent();
            _sb.Write("}");
        }
    }

    private void PrintObjectProperty(TsObjectProperty prop)
    {
        if (prop.Value is TsSpreadExpression spread)
        {
            _sb.Write("...");
            PrintOperand(spread.Expression, TsOperandSlot.AssignmentExpression);
        }
        else if (prop.Shorthand)
        {
            _sb.Write(prop.Key);
        }
        else
        {
            PrintPropertyKey(prop.Key);
            _sb.Write(": ");
            PrintListElement(prop.Value);
        }
    }

    // ─── Shared helpers ─────────────────────────────────────

    private void PrintParameters(IReadOnlyList<TsParameter> parameters)
    {
        _sb.WriteList(parameters, p =>
        {
            if (p.Rest)
                _sb.Write("...");
            _sb.Write(p.Name);
            // `?` and `= expr` both convey optionality, but TS
            // forbids combining them — bias toward the default
            // expression when both are set so the call site sees
            // the seeded value instead of `undefined`.
            if (p.Optional && p.DefaultValue is null)
                _sb.Write("?");
            // Null Type → skip the annotation entirely (used by lambdas whose source
            // parameter is an [External] type, so TypeScript can infer from context).
            if (p.Type is not null)
            {
                _sb.Write(": ");
                PrintType(p.Type);
            }
            if (p.DefaultValue is not null)
            {
                _sb.Write(" = ");
                PrintExpression(p.DefaultValue);
            }
        });
    }

    private void PrintTypeParameters(IReadOnlyList<TsTypeParameter>? typeParams)
    {
        if (typeParams is not { Count: > 0 })
            return;
        _sb.Write("<");
        _sb.WriteList(typeParams, tp =>
        {
            _sb.Write(tp.Name);
            if (tp.Constraint is not null)
            {
                _sb.Write(" extends ");
                PrintType(tp.Constraint);
            }
        });
        _sb.Write(">");
    }

    /// <summary>
    /// Writes the <c>private</c> / <c>protected</c> modifier; public members
    /// carry no keyword.
    /// </summary>
    private void PrintAccessibility(TsAccessibility accessibility)
    {
        if (accessibility is TsAccessibility.Private or TsAccessibility.Protected)
            _sb.Write($"{accessibility.ToKeyword()} ");
    }

    private void PrintBody(IReadOnlyList<TsStatement> body)
    {
        _sb.WriteBlock(() => PrintStatementList(body));
    }

    public static string RenderType(TsType type) => new Printer().PrintTypeOnce(type);

    public static string RenderExpression(TsExpression expression) =>
        new Printer().PrintExpressionFragment(expression);
}
