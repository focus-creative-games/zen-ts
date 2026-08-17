/**
 * ZenTS ambient types (Docs/spec/05-LIB.md, 14-TYPESCRIPT.md).
 * Runtime still loads JS only; this file is editor/tsc only.
 */

declare const zents: ZenTS.Lib;

declare const CSharp: ZenTS.CSharpRoot;

declare const console: {
  log(...args: unknown[]): void;
  error(...args: unknown[]): void;
  warn(...args: unknown[]): void;
};

declare namespace ZenTS {
  /** CLR type object (STO). Same identity as CSharp[asm][fullName]. */
  type TypeObject = (new (...args: any[]) => any) & {
    readonly __zents_type_name?: string;
    readonly __fullname?: string;
    readonly __assembly?: string;
    readonly __name?: string;
    [key: string]: any;
  };

  type TypeArg = TypeObject | string;

  interface CSharpAssembly {
    [typeFullName: string]: TypeObject;
  }

  interface CSharpRoot {
    mscorlib: CSharpAssembly;
    [assembly: string]: CSharpAssembly;
  }

  /** SZ array instance (get/set/length). Not a JS Array; `T[]` is only `zents.to_array`. */
  interface SzArray<T = unknown> {
    readonly length: number;
    get(index: number): T;
    set(index: number, value: T): void;
  }

  /** By-ref / opaque slot. */
  interface OpaqueHandle<T = unknown> {
    readonly __zents_opaque?: true;
    readonly __element?: T;
  }

  /**
   * Open generic type definition. `N` is CLR arity (`List`1` → 1).
   * `new List$1()` is a type error; close with `zents.make_generic_type`.
   */
  interface GenericDef<N extends number = number> {
    readonly __zents_generic_arity?: N;
    readonly __zents_type_name?: string;
    new (...args: never[]): never;
  }

  interface Lib {
    types: {
      void: string;
      bool: string;
      boolean: string;
      char: string;
      byte: string;
      sbyte: string;
      short: string;
      ushort: string;
      int: string;
      int32: string;
      uint: string;
      long: string;
      ulong: string;
      float: string;
      float32: string;
      double: string;
      float64: string;
      decimal: string;
      intptr: string;
      uintptr: string;
      object: string;
      string: string;
    };

    typeof(typeObject: TypeObject): unknown;
    get_type_from_name(typeFullName: string): TypeObject;

    make_generic_type(genericBaseType: GenericDef<1>, typeArg: TypeArg): TypeObject;
    make_generic_type(genericBaseType: GenericDef<2>, t1: TypeArg, t2: TypeArg): TypeObject;
    make_generic_type(genericBaseType: GenericDef<3>, t1: TypeArg, t2: TypeArg, t3: TypeArg): TypeObject;
    make_generic_type(genericBaseType: TypeObject, ...typeArgs: TypeArg[]): TypeObject;

    make_generic_method(genericMethodBase: unknown, ...typeArgs: TypeArg[]): Function;
    make_szarray_type(elementType: TypeArg): TypeObject;
    make_mdarray_type(elementType: TypeArg, rank: number): TypeObject;
    new_szarray_by_element_type<T = unknown>(elementType: TypeArg, length: number): SzArray<T>;
    new_szarray_by_szarray_type<T = unknown>(szarrayType: TypeObject, length: number): SzArray<T>;
    new_mdarray_by_spec(elementType: TypeArg, lowbounds: number[], sizes: number[]): unknown;
    new_mdarray_by_mdarray_type(mdarrayType: TypeObject, lowbounds: number[], sizes: number[]): unknown;
    to_bytes(szarray: SzArray<unknown>): Uint8Array | string;
    to_array<T = unknown>(szarray: SzArray<T>): T[];
    to_delegate(func: Function, delegateType: TypeObject): unknown;
    get_opaquevalue<T = unknown>(handle: OpaqueHandle<T>): T;
    set_opaquevalue<T = unknown>(handle: OpaqueHandle<T>, value: T): void;
    to_user_data(handle: OpaqueHandle<unknown>): unknown;
    box(typeArg: TypeArg, value: unknown): unknown;
    unbox(boxedValue: unknown): unknown;
    cast(obj: unknown, targetType: TypeArg): unknown;
    register_method(aliasName: string, methodOrClosure: Function): void;
    signature(...typeArgs: TypeArg[]): string;
  }
}
