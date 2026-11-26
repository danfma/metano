namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A TypeScript class field: name: Type = initializer;
/// </summary>
/// <param name="DefinitelyAssigned">When true the field carries the definite-assignment
/// assertion (<c>name!: Type;</c>), telling <c>strictPropertyInitialization</c> that code
/// outside the constructor assigns it. Only meaningful without an
/// <see cref="Initializer"/>.</param>
/// <param name="Abstract">An abstract property declaration (<c>abstract readonly name: Type;</c>):
/// it has no storage and no initializer, and a derived class implements it with a field,
/// a parameter property or an accessor.</param>
/// <param name="JSDoc">The JSDoc comment printed ahead of the member — the
/// <c>/** @internal *&#47;</c> tag of the assembly-wide visibility band.</param>
/// <param name="Override">The field redeclares a base member
/// (<c>override readonly name: string = "…";</c>), which
/// <c>noImplicitOverride</c> requires to say so.</param>
/// <param name="StoreInitializer">The <see cref="Initializer"/> for a constructor store:
/// the same value over temporaries the store's statement list declares instead of an
/// immediately invoked function; <c>null</c> when the initializer needs none.</param>
public sealed record TsFieldMember(
    string Name,
    TsType Type,
    TsExpression? Initializer = null,
    bool Readonly = false,
    bool Static = false,
    bool Optional = false,
    TsAccessibility Accessibility = TsAccessibility.Public,
    bool DefinitelyAssigned = false,
    bool Abstract = false,
    TsJSDoc? JSDoc = null,
    bool Override = false,
    bool Declare = false,
    TsStoreInitializer? StoreInitializer = null
) : TsClassMember;

/// <summary>
/// A field initializer as a constructor statement stores it: <see cref="Value"/> assigns
/// the temporaries <see cref="Temporaries"/> declares (<c>let $t0, $t1;</c>) inline.
/// </summary>
public sealed record TsStoreInitializer(TsExpression Value, IReadOnlyList<TsStatement> Temporaries);
