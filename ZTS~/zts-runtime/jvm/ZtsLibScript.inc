// Copyright 2026 Code Philosophy
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

/* Embedded ztslib.js - keep in sync with ZTS~/jslib/ztslib.js */
static const char kZtsLibJs[] = R"zts(globalThis.zts = globalThis.zts || {};

/** console.* -> Unity log (Docs/spec/14-TYPESCRIPT.md). Host binds __zts_print(level, line). */
(function installConsole() {
  function stringify(v) {
    if (v === null) {
      return "null";
    }
    if (v === undefined) {
      return "undefined";
    }
    try {
      return String(v);
    } catch (e) {
      return "[unprintable]";
    }
  }

  function write(level, args) {
    var parts = [];
    for (var i = 0; i < args.length; i++) {
      parts.push(stringify(args[i]));
    }
    var line = parts.join(" ");
    if (typeof globalThis.__zts_print === "function") {
      globalThis.__zts_print(level, line);
    }
  }

  if (typeof globalThis.console === "undefined") {
    globalThis.console = {
      log: function () {
        write(0, arguments);
      },
      info: function () {
        write(0, arguments);
      },
      warn: function () {
        write(1, arguments);
      },
      error: function () {
        write(2, arguments);
      }
    };
  }
})();

zts.typeof = function (typeObject) {
  return globalThis.__zts_typeof(typeObject);
};

zts.get_type_from_name = function (typeFullName) {
  return globalThis.__zts_get_type_from_name(typeFullName);
};

zts.make_generic_type = function (genericType) {
  var args = Array.prototype.slice.call(arguments, 1);
  if (args.length === 1 && Array.isArray(args[0])) {
    args = args[0];
  }
  return globalThis.__zts_make_generic_type(genericType, args);
};

zts.make_generic_method = function (genericMethodBase) {
  var args = Array.prototype.slice.call(arguments, 1);
  if (args.length === 1 && Array.isArray(args[0])) {
    args = args[0];
  }
  return globalThis.__zts_make_generic_method(genericMethodBase, args);
};

zts.make_szarray_type = function (elementType) {
  return globalThis.__zts_make_szarray_type(elementType);
};

zts.new_szarray_by_element_type = function (elementType, length) {
  return globalThis.__zts_new_szarray_by_element_type(elementType, length);
};

zts.new_szarray_by_szarray_type = function (szarrayType, length) {
  return globalThis.__zts_new_szarray_by_szarray_type(szarrayType, length);
};

zts.make_mdarray_type = function (elementType, rank) {
  return globalThis.__zts_make_mdarray_type(elementType, rank);
};

zts.new_mdarray_by_spec = function (elementType, lowbounds, sizes) {
  return globalThis.__zts_new_mdarray_by_spec(elementType, lowbounds, sizes);
};

zts.new_mdarray_by_mdarray_type = function (mdarrayType, lowbounds, sizes) {
  return globalThis.__zts_new_mdarray_by_mdarray_type(mdarrayType, lowbounds, sizes);
};

zts.to_array = function (szarray) {
  return globalThis.__zts_to_array(szarray);
};

zts.to_bytes = function (szarray) {
  return globalThis.__zts_to_bytes(szarray);
};

zts.to_delegate = function (func, delegateType) {
  return globalThis.__zts_to_delegate(func, delegateType);
};

zts.get_opaquevalue = function (opaqueHandle) {
  return globalThis.__zts_get_opaquevalue(opaqueHandle);
};

zts.set_opaquevalue = function (opaqueHandle, newValue) {
  return globalThis.__zts_set_opaquevalue(opaqueHandle, newValue);
};

zts.to_user_data = function (opaqueHandle) {
  return globalThis.__zts_to_user_data(opaqueHandle);
};

zts.box = function (typeArg, value) {
  return globalThis.__zts_box(typeArg, value);
};

zts.unbox = function (boxedValue) {
  return globalThis.__zts_unbox(boxedValue);
};

zts.cast = function (obj, targetType) {
  return globalThis.__zts_cast(obj, targetType);
};

zts.register_method = function (aliasName, methodOrClosure) {
  return globalThis.__zts_register_method(aliasName, methodOrClosure);
};

zts.signature = function () {
  var args = Array.prototype.slice.call(arguments);
  if (args.length === 1 && Array.isArray(args[0])) {
    args = args[0];
  }
  return globalThis.__zts_create_signature(args);
};

zts.types = {
  void: "System.Void",
  bool: "System.Boolean",
  char: "System.Char",
  byte: "System.Byte",
  sbyte: "System.SByte",
  short: "System.Int16",
  ushort: "System.UInt16",
  int: "System.Int32",
  int32: "System.Int32",
  uint: "System.UInt32",
  long: "System.Int64",
  ulong: "System.UInt64",
  float: "System.Single",
  float32: "System.Single",
  double: "System.Double",
  float64: "System.Double",
  boolean: "System.Boolean",
  decimal: "System.Decimal",
  intptr: "System.IntPtr",
  uintptr: "System.UIntPtr",
  object: "System.Object",
  string: "System.String"
};

(function installCSharpProxy() {
  function makeTypeProxy(asmName) {
    return new Proxy(
      {},
      {
        get: function (target, prop) {
          if (typeof prop !== "string") {
            return undefined;
          }
          if (Object.prototype.hasOwnProperty.call(target, prop)) {
            return target[prop];
          }
          var ty = globalThis.__zts_resolve_type(asmName, prop);
          target[prop] = ty;
          return ty;
        }
      }
    );
  }

  globalThis.CSharp = new Proxy(
    {},
    {
      get: function (target, prop) {
        if (typeof prop !== "string") {
          return undefined;
        }
        if (Object.prototype.hasOwnProperty.call(target, prop)) {
          return target[prop];
        }
        globalThis.__zts_ensure_assembly(prop);
        var asmProxy = makeTypeProxy(prop);
        target[prop] = asmProxy;
        return asmProxy;
      }
    }
  );
})();

/** Strict member miss for STO / IEO (Docs/spec/metatable/02-INDEX). */
globalThis.__zts_wrap_miss = function (target) {
  if (!target || (typeof target !== "object" && typeof target !== "function")) {
    return target;
  }
  return new Proxy(target, {
    get: function (t, p, recv) {
      if (typeof p === "symbol") {
        return Reflect.get(t, p, recv);
      }
      var key = String(p);
      if (
        key === "prototype" ||
        key === "constructor" ||
        key === "name" ||
        key === "length" ||
        key.indexOf("__zts_") === 0
      ) {
        return Reflect.get(t, p, recv);
      }
      if (Reflect.has(t, p)) {
        return Reflect.get(t, p, recv);
      }
      throw new Error("zts: member not found: " + key);
    },
    set: function (t, p, v, recv) {
      if (typeof p === "symbol") {
        return Reflect.set(t, p, v, recv);
      }
      var key = String(p);
      if (Reflect.has(t, p)) {
        if (!Reflect.set(t, p, v, recv)) {
          throw new Error("zts: not writable: " + key);
        }
        return true;
      }
      throw new Error("zts: member not found: " + key);
    }
  });
};

/** CLR Delegate exotic [[Call]] → Invoke (Docs/spec/marshal/09-FUNCTION §3.2). */
globalThis.__zts_wrap_delegate_call = function (del) {
  if (!del || (typeof del !== "object" && typeof del !== "function")) {
    return del;
  }
  var fn = function () {
    return del.Invoke.apply(del, arguments);
  };
  return new Proxy(fn, {
    apply: function (_t, _thisArg, args) {
      return del.Invoke.apply(del, args);
    },
    get: function (_t, p, recv) {
      if (p === "length" || p === "name" || p === "prototype") {
        return Reflect.get(_t, p, recv);
      }
      return Reflect.get(del, p, recv);
    },
    has: function (_t, p) {
      return Reflect.has(del, p);
    },
    set: function (_t, p, v, recv) {
      return Reflect.set(del, p, v, recv);
    }
  });
};)zts";
