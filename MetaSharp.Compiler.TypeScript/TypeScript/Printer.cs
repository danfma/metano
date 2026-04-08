using MetaSharp.TypeScript.AST;

namespace MetaSharp.TypeScript;

/// <summary>
/// Walks a TypeScript AST and produces formatted source text.
/// Modeled after the tsc emitter: AST in, text out.
/// </summary>
public sealed class Printer(string indent = "  ")
{
    private readonly IndentedStringBuilder _sb = new(indent);

    public string Print(TsSourceFile file)
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
    /// Decides if a blank line should separate two consecutive top-level statements.
    /// Imports/re-exports are grouped together with no blank lines. Distinct kinds
    /// (types vs functions vs constants) get a blank line between them. Same-kind
    /// items (e.g., consecutive functions) also get a blank line for readability.
    /// </summary>
    private static bool NeedsBlankLineBetween(TsTopLevel prev, TsTopLevel next)
    {
        // Import / re-export block: no blank line between them, but blank line before
        // anything else.
        var prevIsImport = prev is TsImport or TsReExport;
        var nextIsImport = next is TsImport or TsReExport;
        if (prevIsImport && nextIsImport) return false;
        if (prevIsImport != nextIsImport) return true;

        // For non-imports, separate everything by blank lines (types, functions, constants)
        return true;
    }

    // ─── Top-level ──────────────────────────────────────────

    private void PrintTopLevel(TsTopLevel node)
    {
        switch (node)
        {
            case TsImport n: PrintImport(n); break;
            case TsReExport n: PrintReExport(n); break;
            case TsTypeAlias n: PrintTypeAlias(n); break;
            case TsInterface n: PrintInterface(n); break;
            case TsFunction n: PrintFunction(n); break;
            case TsEnum n: PrintEnum(n); break;
            case TsConstObject n: PrintConstObject(n); break;
            case TsNamespaceDeclaration n: PrintNamespace(n); break;
            case TsClass n: PrintClass(n); break;
        }
    }

    private void PrintImport(TsImport import)
    {
        _sb.Write("import ");
        if (import.TypeOnly) _sb.Write("type ");
        _sb.Write("{ ");
        _sb.Write(string.Join(", ", import.Names));
        _sb.Write(" } from ");
        _sb.WriteQuoted(import.From);
        _sb.Write(";");
    }

    private void PrintReExport(TsReExport reExport)
    {
        _sb.Write("export ");
        if (reExport.TypeOnly) _sb.Write("type ");
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
        if (alias.Exported) _sb.Write("export ");
        _sb.Write("type ");
        _sb.Write(alias.Name);
        _sb.Write(" = ");
        PrintType(alias.Type);
        _sb.Write(";");
    }

    private void PrintInterface(TsInterface iface)
    {
        if (iface.Exported) _sb.Write("export ");
        _sb.Write("interface ");
        _sb.Write(iface.Name);
        PrintTypeParameters(iface.TypeParameters);
        _sb.WriteBlock(() =>
        {
            foreach (var prop in iface.Properties)
            {
                PrintAccessibility(prop.Accessibility);
                if (prop.Readonly) _sb.Write("readonly ");
                _sb.Write(prop.Name);
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

    private void PrintFunction(TsFunction func)
    {
        if (func.Exported) _sb.Write("export ");
        if (func.Async) _sb.Write("async ");
        _sb.Write(func.Generator ? "function* " : "function ");
        _sb.Write(func.Name);
        PrintTypeParameters(func.TypeParameters);
        _sb.Write("(");
        PrintParameters(func.Parameters);
        _sb.Write("): ");
        PrintType(func.ReturnType);
        PrintBody(func.Body);
    }

    private void PrintEnum(TsEnum tsEnum)
    {
        if (tsEnum.Exported) _sb.Write("export ");
        _sb.Write("enum ");
        _sb.Write(tsEnum.Name);
        _sb.WriteBlock(() =>
        {
            foreach (var member in tsEnum.Members)
            {
                _sb.Write(member.Name);
                if (member.Value is not null)
                {
                    _sb.Write(" = ");
                    PrintExpression(member.Value);
                }

                _sb.Write(",");
                _sb.WriteLn();
            }
        });
    }

    private void PrintNamespace(TsNamespaceDeclaration ns)
    {
        if (ns.Exported) _sb.Write("export ");
        _sb.Write("namespace ");
        _sb.Write(ns.Name);
        _sb.WriteBlock(() =>
        {
            // Functions (used by InlineWrapper companions)
            for (var i = 0; i < ns.Functions.Count; i++)
            {
                if (i > 0) _sb.WriteLn();
                PrintFunction(ns.Functions[i]);
                _sb.WriteLn();
            }

            // Arbitrary nested members (used by nested types)
            if (ns.Members is not null)
            {
                var startIdx = ns.Functions.Count;
                for (var i = 0; i < ns.Members.Count; i++)
                {
                    if (startIdx + i > 0) _sb.WriteLn();
                    PrintTopLevel(ns.Members[i]);
                    _sb.WriteLn();
                }
            }
        });
    }

    private void PrintConstObject(TsConstObject constObj)
    {
        if (constObj.Exported) _sb.Write("export ");
        _sb.Write("const ");
        _sb.Write(constObj.Name);
        _sb.Write(" =");
        _sb.WriteBlock(() =>
        {
            foreach (var (key, value) in constObj.Entries)
            {
                _sb.Write(key);
                _sb.Write(": ");
                PrintExpression(value);
                _sb.Write(",");
                _sb.WriteLn();
            }
        });
        _sb.Write(" as const;");
    }

    // ─── Class ──────────────────────────────────────────────

    private void PrintClass(TsClass tsClass)
    {
        if (tsClass.Exported) _sb.Write("export ");
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
        var others = tsClass.Members
            .Where(m => m is not (TsFieldMember or TsGetterMember or TsSetterMember or TsMethodMember))
            .ToList();

        _sb.WriteBlock(() =>
        {
            var firstGroup = true;

            void PrintGroup(IReadOnlyList<TsClassMember> members)
            {
                if (members.Count == 0) return;
                if (!firstGroup) _sb.WriteLn();
                firstGroup = false;
                for (var i = 0; i < members.Count; i++)
                {
                    if (i > 0) _sb.WriteLn();
                    PrintClassMember(members[i]);
                }
            }

            // Fields first
            PrintGroup(fields);

            // Constructor
            if (tsClass.Constructor is not null)
            {
                if (!firstGroup) _sb.WriteLn();
                firstGroup = false;
                PrintConstructor(tsClass.Constructor);
            }

            // Getters / setters
            PrintGroup(accessors);

            // Methods
            PrintGroup(methods);

            // Anything else
            PrintGroup(others);
        });
    }

    private void PrintConstructor(TsConstructor ctor)
    {
        if (ctor.Overloads is { Count: > 0 })
        {
            // Print overload signatures first
            foreach (var overload in ctor.Overloads)
            {
                _sb.Write("constructor(");
                _sb.WriteList(overload.Parameters, PrintConstructorParam);
                _sb.Write(");");
                _sb.WriteLn();
            }

            // Print dispatcher implementation
            _sb.Write("constructor(");
            _sb.WriteList(ctor.Parameters, PrintConstructorParam);
            _sb.Write(")");
            PrintBody(ctor.Body);
        }
        else
        {
            // Single constructor (no overloads)
            _sb.Write("constructor(");
            _sb.WriteList(ctor.Parameters, PrintConstructorParam);
            _sb.Write(")");

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

    private void PrintConstructorParam(TsConstructorParam p)
    {
        // In TS, constructor params without any modifier (public/readonly/etc) don't create properties.
        // Emit explicit "public" when not readonly but the param should still be a property.
        // None = plain parameter, no property created (used for exception constructors).
        if (p.Accessibility == TsAccessibility.None)
        { /* no modifier — plain parameter */ }
        else if (p.Accessibility == TsAccessibility.Public && !p.Readonly)
            _sb.Write("public ");
        else
            PrintAccessibility(p.Accessibility);
        if (p.Readonly) _sb.Write("readonly ");
        _sb.Write(p.Name);
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
                _sb.Write("get ");
                _sb.Write(getter.Name);
                _sb.Write("(): ");
                PrintType(getter.ReturnType);
                PrintBody(getter.Body);
                _sb.WriteLn();
                break;

            case TsSetterMember setter:
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
                PrintAccessibility(field.Accessibility);
                if (field.Readonly) _sb.Write("readonly ");
                _sb.Write(field.Name);
                _sb.Write(": ");
                PrintType(field.Type);
                if (field.Initializer is not null)
                {
                    _sb.Write(" = ");
                    PrintExpression(field.Initializer);
                }
                _sb.Write(";");
                _sb.WriteLn();
                break;

            case TsMethodMember method:
                if (method.Overloads is { Count: > 0 })
                {
                    // Print overload signatures first
                    foreach (var overload in method.Overloads)
                    {
                        PrintAccessibility(method.Accessibility);
                        if (method.Static) _sb.Write("static ");
                        _sb.Write(method.Name);
                        _sb.Write("(");
                        PrintParameters(overload.Parameters);
                        _sb.Write("): ");
                        PrintType(overload.ReturnType);
                        _sb.Write(";");
                        _sb.WriteLn();
                    }
                }

                // Print the implementation (or single method)
                PrintAccessibility(method.Accessibility);
                if (method.Static) _sb.Write("static ");
                if (method.Async) _sb.Write("async ");
                if (method.Generator) _sb.Write("*");
                _sb.Write(method.Name);
                PrintTypeParameters(method.TypeParameters);
                _sb.Write("(");
                PrintParameters(method.Parameters);
                _sb.Write("): ");
                PrintType(method.ReturnType);
                PrintBody(method.Body);
                _sb.WriteLn();
                break;
        }
    }

    // ─── Types ──────────────────────────────────────────────

    private void PrintType(TsType type)
    {
        switch (type)
        {
            case TsNumberType: _sb.Write("number"); break;
            case TsStringType: _sb.Write("string"); break;
            case TsBooleanType: _sb.Write("boolean"); break;
            case TsVoidType: _sb.Write("void"); break;
            case TsBigIntType: _sb.Write("bigint"); break;
            case TsAnyType: _sb.Write("any"); break;

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
                PrintType(array.ElementType);
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
                _sb.WriteList(union.Types, PrintType, " | ");
                break;

            case TsIntersectionType intersection:
                _sb.WriteList(intersection.Types, PrintType, " & ");
                break;

            case TsTupleType tuple:
                _sb.Write("[");
                _sb.WriteList(tuple.Elements, PrintType);
                _sb.Write("]");
                break;

            case TsTypePredicateType predicate:
                _sb.Write(predicate.ParameterName);
                _sb.Write(" is ");
                PrintType(predicate.Type);
                break;
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
                _sb.WriteBlock(() => _sb.WriteLines(ifStmt.Then, PrintStatement));
                if (ifStmt.Else is { Count: > 0 })
                {
                    _sb.Write(" else");
                    _sb.WriteBlock(() => _sb.WriteLines(ifStmt.Else, PrintStatement));
                }

                break;

            case TsThrowStatement throwStmt:
                _sb.Write("throw ");
                PrintExpression(throwStmt.Expression);
                _sb.Write(";");
                break;

            case TsVariableDeclaration varDecl:
                _sb.Write(varDecl.Const ? "const " : "let ");
                _sb.Write(varDecl.Name);
                _sb.Write(" = ");
                PrintExpression(varDecl.Initializer);
                _sb.Write(";");
                break;

            case TsExpressionStatement exprStmt:
                PrintExpression(exprStmt.Expression);
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
                        _sb.WriteLines(c.Body, PrintStatement);
                        _sb.Dedent();
                    }
                });
                break;
        }
    }

    // ─── Expressions ────────────────────────────────────────

    private void PrintExpression(TsExpression expr)
    {
        switch (expr)
        {
            case TsIdentifier id:
                _sb.Write(id.Name);
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
                    _sb.Write(tmpl.Quasis[i]);
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
                PrintExpression(bin.Left);
                _sb.Write($" {bin.Operator} ");
                PrintExpression(bin.Right);
                break;

            case TsPropertyAccess access:
                PrintExpression(access.Object);
                _sb.Write(".");
                _sb.Write(access.Property);
                break;

            case TsCallExpression call:
                PrintExpression(call.Callee);
                _sb.Write("(");
                _sb.WriteList(call.Arguments, PrintExpression);
                _sb.Write(")");
                break;

            case TsObjectLiteral obj:
                PrintObjectLiteral(obj);
                break;

            case TsSpreadExpression spread:
                _sb.Write("...");
                PrintExpression(spread.Expression);
                break;

            case TsElementAccess elemAccess:
                PrintExpression(elemAccess.Object);
                _sb.Write("[");
                PrintExpression(elemAccess.Index);
                _sb.Write("]");
                break;

            case TsArrayLiteral arrayLit:
                _sb.Write("[");
                _sb.WriteList(arrayLit.Elements, PrintExpression);
                _sb.Write("]");
                break;

            case TsNewExpression newExpr:
                _sb.Write("new ");
                PrintExpression(newExpr.Callee);
                _sb.Write("(");
                _sb.WriteList(newExpr.Arguments, PrintExpression);
                _sb.Write(")");
                break;

            case TsAwaitExpression awaitExpr:
                _sb.Write("await ");
                PrintExpression(awaitExpr.Expression);
                break;

            case TsArrowFunction arrow:
                if (arrow.Async) _sb.Write("async ");
                _sb.Write("(");
                PrintParameters(arrow.Parameters);
                _sb.Write(") => ");
                // Single return statement → concise expression body
                if (arrow.Body is [TsReturnStatement { Expression: { } returnExpr }])
                {
                    PrintExpression(returnExpr);
                }
                else
                {
                    _sb.WriteBlock(() => _sb.WriteLines(arrow.Body, PrintStatement));
                }
                break;

            case TsUnaryExpression unary:
                _sb.Write(unary.Operator);
                PrintExpression(unary.Operand);
                break;

            case TsParenthesized paren:
                _sb.Write("(");
                PrintExpression(paren.Expression);
                _sb.Write(")");
                break;

            case TsCastExpression cast:
                PrintExpression(cast.Expression);
                _sb.Write(" as ");
                PrintType(cast.Type);
                break;

            case TsConditionalExpression cond:
                PrintExpression(cond.Condition);
                _sb.Write(" ? ");
                PrintExpression(cond.WhenTrue);
                _sb.Write(" : ");
                PrintExpression(cond.WhenFalse);
                break;
        }
    }

    /// <summary>
    /// Renders a <see cref="TsTemplate"/> by walking the template string and emitting
    /// each chunk in turn: literal text is written verbatim, while
    /// <c>$this</c>/<c>$N</c>/<c>$TN</c> placeholders trigger a substitution. Value
    /// placeholders (<c>$this</c>, <c>$N</c>) recurse via <see cref="PrintExpression"/>
    /// so complex argument shapes (nested calls, lambdas, binary operators) round-trip
    /// correctly. Type placeholders (<c>$TN</c>) are plain identifier names and emit
    /// as raw text.
    /// </summary>
    private void PrintTemplate(TsTemplate template)
    {
        var text = template.Template;
        var i = 0;
        while (i < text.Length)
        {
            // Look for the next placeholder; everything before it is literal text.
            var dollar = text.IndexOf('$', i);
            if (dollar < 0)
            {
                _sb.Write(text[i..]);
                break;
            }

            if (dollar > i)
                _sb.Write(text[i..dollar]);

            // Try $this first.
            if (dollar + 5 <= text.Length
                && text.AsSpan(dollar, 5).SequenceEqual("$this".AsSpan()))
            {
                if (template.Receiver is not null)
                    PrintExpression(template.Receiver);
                i = dollar + 5;
                continue;
            }

            // Then $T<n> (type-argument name placeholder).
            if (dollar + 2 < text.Length && text[dollar + 1] == 'T' && char.IsDigit(text[dollar + 2]))
            {
                var typeArgStart = dollar + 2;
                var typeArgEnd = typeArgStart;
                while (typeArgEnd < text.Length && char.IsDigit(text[typeArgEnd]))
                    typeArgEnd++;

                if (int.TryParse(text.AsSpan(typeArgStart, typeArgEnd - typeArgStart), out var typeArgIndex)
                    && typeArgIndex < template.TypeArgumentNames.Count)
                {
                    _sb.Write(template.TypeArgumentNames[typeArgIndex]);
                    i = typeArgEnd;
                    continue;
                }
            }

            // Then $0, $1, … — read the integer suffix.
            var argStart = dollar + 1;
            var argEnd = argStart;
            while (argEnd < text.Length && char.IsDigit(text[argEnd]))
                argEnd++;

            if (argEnd > argStart
                && int.TryParse(text.AsSpan(argStart, argEnd - argStart), out var argIndex)
                && argIndex < template.Arguments.Count)
            {
                PrintExpression(template.Arguments[argIndex]);
                i = argEnd;
                continue;
            }

            // Lone $ or unrecognized placeholder — emit verbatim and advance one char.
            _sb.Write("$");
            i = dollar + 1;
        }
    }

    private void PrintObjectLiteral(TsObjectLiteral obj)
    {
        if (obj.Properties.Count == 0)
        {
            _sb.Write("{}");
            return;
        }

        var hasSpread = obj.Properties.Any(p => p.Value is TsSpreadExpression);
        if (obj.Properties.Count <= 3 && !hasSpread)
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
            PrintExpression(spread.Expression);
        }
        else if (prop.Shorthand)
        {
            _sb.Write(prop.Key);
        }
        else
        {
            _sb.Write(prop.Key);
            _sb.Write(": ");
            PrintExpression(prop.Value);
        }
    }

    // ─── Shared helpers ─────────────────────────────────────

    private void PrintParameters(IReadOnlyList<TsParameter> parameters)
    {
        _sb.WriteList(parameters, p =>
        {
            _sb.Write(p.Name);
            // Null Type → skip the annotation entirely (used by lambdas whose source
            // parameter is a [NoEmit] type, so TypeScript can infer from context).
            if (p.Type is not null)
            {
                _sb.Write(": ");
                PrintType(p.Type);
            }
        });
    }

    private void PrintTypeParameters(IReadOnlyList<TsTypeParameter>? typeParams)
    {
        if (typeParams is not { Count: > 0 }) return;
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

    private void PrintAccessibility(TsAccessibility accessibility)
    {
        switch (accessibility)
        {
            case TsAccessibility.Private: _sb.Write("private "); break;
            case TsAccessibility.Protected: _sb.Write("protected "); break;
        }
    }

    private void PrintBody(IReadOnlyList<TsStatement> body)
    {
        _sb.WriteBlock(() => _sb.WriteLines(body, PrintStatement));
    }
}
