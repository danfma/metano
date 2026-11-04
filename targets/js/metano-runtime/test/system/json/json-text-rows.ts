/**
 * JSON texts and what System.Text.Json's reader does with them, measured on
 * .NET 10.0.12 (System.Text.Json 10.0.0.0, `JsonSerializer.Deserialize` of a
 * `string`) for #457: `error` is the `JsonException` message without its
 * `Path` / `LineNumber` suffix (DECIDED-5); a row without one is valid, and
 * `JSON.parse` agrees on every row. Characters other than printable ASCII
 * are printed as the first byte of their UTF-8 encoding (`'0xC3'` for `é`).
 * Left out: a text that ends inside 66 open arrays, where STJ reports its
 * depth limit and the runtime the open container (FR-016's limit belongs to
 * the contract walk, which never sees an invalid text).
 */
export type JsonTextRow = {
  readonly text: string;
  readonly error?: string;
};

export const JSON_TEXT_ROWS: readonly JsonTextRow[] = [
  {
    text: "",
    error:
      "The input does not contain any JSON tokens. Expected the input to start with a valid JSON token, when isFinalBlock is true.",
  },
  {
    text: " ",
    error:
      "The input does not contain any JSON tokens. Expected the input to start with a valid JSON token, when isFinalBlock is true.",
  },
  { text: "x", error: "'x' is an invalid start of a value." },
  {
    text: "{",
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  {
    text: "[",
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  { text: "}", error: "'}' is an invalid start of a value." },
  { text: "]", error: "']' is an invalid start of a value." },
  {
    text: "[1,]",
    error:
      "The JSON array contains a trailing comma at the end which is not supported in this mode. Change the reader options.",
  },
  {
    text: '{"a":1,}',
    error:
      "The JSON object contains a trailing comma at the end which is not supported in this mode. Change the reader options.",
  },
  { text: "[1 2]", error: "'2' is invalid after a value. Expected either ',', '}', or ']'." },
  { text: '{"a" 1}', error: "'1' is invalid after a property name. Expected a ':'." },
  {
    text: '{"a":1 "b":2}',
    error: "'\"' is invalid after a value. Expected either ',', '}', or ']'.",
  },
  { text: "{1:1}", error: "'1' is an invalid start of a property name. Expected a '\"'." },
  { text: "{a:1}", error: "'a' is an invalid start of a property name. Expected a '\"'." },
  { text: "{'a':1}", error: "''' is an invalid start of a property name. Expected a '\"'." },
  { text: '{"a"}', error: "'}' is invalid after a property name. Expected a ':'." },
  { text: '{"a":}', error: "'}' is an invalid start of a value." },
  { text: "[,1]", error: "',' is an invalid start of a value." },
  { text: "[1,,2]", error: "',' is an invalid start of a value." },
  { text: "tru", error: "'tru' is an invalid JSON literal. Expected the literal 'true'." },
  { text: "true1", error: "'1' is invalid after a single JSON value. Expected end of data." },
  { text: "nul", error: "'nul' is an invalid JSON literal. Expected the literal 'null'." },
  { text: "nulll", error: "'l' is invalid after a single JSON value. Expected end of data." },
  { text: "fals", error: "'fals' is an invalid JSON literal. Expected the literal 'false'." },
  { text: "falsey", error: "'y' is invalid after a single JSON value. Expected end of data." },
  { text: "True", error: "'T' is an invalid start of a value." },
  { text: '"abc', error: "Expected end of string, but instead reached end of data." },
  {
    text: '"\\x"',
    error:
      "'x' is an invalid escapable character within a JSON string. The string should be correctly escaped.",
  },
  {
    text: '"\\u12G4"',
    error:
      "'G' is not a hex digit following '\\u' within a JSON string. The string should be correctly escaped.",
  },
  {
    text: '"\\u12"',
    error:
      "'\"' is not a hex digit following '\\u' within a JSON string. The string should be correctly escaped.",
  },
  {
    text: '"a\nb"',
    error: "'0x0A' is invalid within a JSON string. The string should be correctly escaped.",
  },
  {
    text: '"a\tb"',
    error: "'0x09' is invalid within a JSON string. The string should be correctly escaped.",
  },
  {
    text: '"a\u0001b"',
    error: "'0x01' is invalid within a JSON string. The string should be correctly escaped.",
  },
  { text: '"\\uD800"' },
  { text: '"\\uDC00"' },
  { text: '"\\uD83D\\uDE00"' },
  { text: '"😀"' },
  { text: '""' },
  { text: "1 2", error: "'2' is invalid after a single JSON value. Expected end of data." },
  { text: "{} {}", error: "'{' is invalid after a single JSON value. Expected end of data." },
  { text: "[] x", error: "'x' is invalid after a single JSON value. Expected end of data." },
  { text: "1 /*c*/", error: "'/' is invalid after a single JSON value. Expected end of data." },
  { text: "//c\n1", error: "'/' is an invalid start of a value." },
  { text: "/*c*/1", error: "'/' is an invalid start of a value." },
  { text: "01", error: "Invalid leading zero before '1'." },
  { text: "-01", error: "Invalid leading zero before '1'." },
  { text: "00", error: "Invalid leading zero before '0'." },
  { text: "1.", error: "Expected a digit ('0'-'9'), but instead reached end of data." },
  {
    text: "1.e5",
    error:
      "'e' is invalid within a number, immediately after a decimal point ('.'). Expected a digit ('0'-'9').",
  },
  { text: ".5", error: "'.' is an invalid start of a value." },
  { text: "-", error: "Expected a digit ('0'-'9'), but instead reached end of data." },
  {
    text: "-a",
    error:
      "'a' is invalid within a number, immediately after a sign character ('+' or '-'). Expected a digit ('0'-'9').",
  },
  { text: "1e", error: "Expected a digit ('0'-'9'), but instead reached end of data." },
  { text: "1e+", error: "Expected a digit ('0'-'9'), but instead reached end of data." },
  {
    text: "1ee",
    error:
      "'e' is invalid within a number, immediately after a sign character ('+' or '-'). Expected a digit ('0'-'9').",
  },
  { text: "1.5.5", error: "'.' is an invalid end of a number. Expected 'E' or 'e'." },
  { text: "0x10", error: "'x' is an invalid end of a number. Expected a delimiter." },
  { text: "1_000", error: "'_' is an invalid end of a number. Expected a delimiter." },
  { text: "+1", error: "'+' is an invalid start of a value." },
  { text: "Infinity", error: "'I' is an invalid start of a value." },
  { text: "NaN", error: "'N' is an invalid start of a value." },
  {
    text: "-Infinity",
    error:
      "'I' is invalid within a number, immediately after a sign character ('+' or '-'). Expected a digit ('0'-'9').",
  },
  { text: "'a'", error: "''' is an invalid start of a value." },
  { text: " 1", error: "'0xC2' is an invalid start of a value." },
  { text: " 1", error: "'0xE2' is an invalid start of a value." },
  { text: "﻿1", error: "'0xEF' is an invalid start of a value." },
  { text: "1 ", error: "'0xC2' is an invalid end of a number. Expected a delimiter." },
  { text: "\t\n\r 1 \t\n\r" },
  { text: "\u000b1", error: "'0x0B' is an invalid start of a value." },
  { text: "\f1", error: "'0x0C' is an invalid start of a value." },
  { text: "é", error: "'0xC3' is an invalid start of a value." },
  { text: "[é]", error: "'0xC3' is an invalid start of a value." },
  { text: '"é"' },
  { text: "[1]]", error: "']' is invalid after a single JSON value. Expected end of data." },
  { text: "{}}", error: "'}' is invalid after a single JSON value. Expected end of data." },
  { text: '["a":1]', error: "':' is invalid after a value. Expected either ',', '}', or ']'." },
  { text: '{"a":1]', error: "']' is invalid without a matching open." },
  { text: "[1}", error: "'}' is invalid without a matching open." },
  { text: '{"a":1,"a":2}' },
  { text: '{"__proto__":1}' },
  { text: '"\\/\\b\\f\\n\\r\\t\\"\\\\"' },
  { text: "1e400" },
  { text: "-1e400" },
  { text: "1e-400" },
  { text: "123456789012345678901234567890" },
  { text: "[1,2", error: "'2' is an invalid end of a number. Expected a delimiter." },
  { text: '{"a":1', error: "'1' is an invalid end of a number. Expected a delimiter." },
  { text: '{"a"', error: "Expected a value, but instead reached end of data." },
  { text: '{"a":[1,{"b":2]}', error: "']' is invalid without a matching open." },
  { text: "[1", error: "'1' is an invalid end of a number. Expected a delimiter." },
  { text: "[12", error: "'2' is an invalid end of a number. Expected a delimiter." },
  { text: "[1.5", error: "'5' is an invalid end of a number. Expected a delimiter." },
  { text: "[1e5", error: "'5' is an invalid end of a number. Expected a delimiter." },
  { text: "[-1", error: "'1' is an invalid end of a number. Expected a delimiter." },
  { text: "[0", error: "'0' is an invalid end of a number. Expected a delimiter." },
  {
    text: "[1,",
    error: "Expected start of a property name or value, but instead reached end of data.",
  },
  {
    text: '["a"',
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  {
    text: '["a",',
    error: "Expected start of a property name or value, but instead reached end of data.",
  },
  {
    text: '{"a":',
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  {
    text: '{"a":1,',
    error: "Expected start of a property name or value, but instead reached end of data.",
  },
  {
    text: "[true",
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  { text: "[tru", error: "'tru' is an invalid JSON literal. Expected the literal 'true'." },
  {
    text: "[null",
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  {
    text: '{"a":"b"',
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  {
    text: "[ ",
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  {
    text: "{ ",
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  {
    text: '{"a":1 ',
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  {
    text: "[1 ",
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  { text: "[-", error: "Expected a digit ('0'-'9'), but instead reached end of data." },
  { text: "[1.", error: "Expected a digit ('0'-'9'), but instead reached end of data." },
  { text: "[1e", error: "Expected a digit ('0'-'9'), but instead reached end of data." },
  { text: "[1e+", error: "Expected a digit ('0'-'9'), but instead reached end of data." },
  { text: '{"a', error: "Expected end of string, but instead reached end of data." },
  { text: '{"a\\', error: "Expected end of string, but instead reached end of data." },
  { text: '"\\', error: "Expected end of string, but instead reached end of data." },
  { text: '"\\u12', error: "Expected end of string, but instead reached end of data." },
  { text: '["\\u12', error: "Expected end of string, but instead reached end of data." },
  { text: "-1" },
  { text: "-0" },
  { text: "0" },
  { text: "1.5e5" },
  { text: " 1 " },
  { text: "[]" },
  { text: "{}" },
  { text: "[1,[2,[3]]]" },
  { text: '{"a":{"b":[]}}' },
  { text: '[{"a":true}]' },
  {
    text: '["a" ',
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  {
    text: '{"a":true',
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  { text: '{"a":nul', error: "'nul' is an invalid JSON literal. Expected the literal 'null'." },
  { text: "[1]x", error: "'x' is invalid after a single JSON value. Expected end of data." },
  { text: "[1] 2", error: "'2' is invalid after a single JSON value. Expected end of data." },
  { text: '"a"1', error: "'1' is invalid after a single JSON value. Expected end of data." },
  { text: '{"":1}' },
  { text: "[0.1e-5]" },
  { text: "[01]", error: "Invalid leading zero before '1'." },
  {
    text: "[-]",
    error:
      "']' is invalid within a number, immediately after a sign character ('+' or '-'). Expected a digit ('0'-'9').",
  },
  { text: "{,}", error: "',' is an invalid start of a property name. Expected a '\"'." },
  { text: '{"a":1,,}', error: "',' is an invalid start of a property name. Expected a '\"'." },
  { text: "[1 , 2 ]" },
  { text: '{ "a" : 1 , "b" : [ ] }' },
  { text: '["\\u00e9\\uD83D\\uDE00"]' },
  { text: "[1 ]", error: "'0xC2' is an invalid end of a number. Expected a delimiter." },
  { text: '{"a":1 }', error: "'0xC2' is an invalid end of a number. Expected a delimiter." },
  { text: '["a"é]', error: "'0xC3' is invalid after a value. Expected either ',', '}', or ']'." },
  { text: "{é}", error: "'0xC3' is an invalid start of a property name. Expected a '\"'." },
  { text: '{"a"é}', error: "'0xC3' is invalid after a property name. Expected a ':'." },
  { text: "[t]", error: "'t]' is an invalid JSON literal. Expected the literal 'true'." },
  { text: "[nulx]", error: "'nulx]' is an invalid JSON literal. Expected the literal 'null'." },
  { text: "[truex]", error: "'x' is invalid after a value. Expected either ',', '}', or ']'." },
  { text: "[true x]", error: "'x' is invalid after a value. Expected either ',', '}', or ']'." },
  { text: "[1x]", error: "'x' is an invalid end of a number. Expected a delimiter." },
  {
    text: "[-x]",
    error:
      "'x' is invalid within a number, immediately after a sign character ('+' or '-'). Expected a digit ('0'-'9').",
  },
  {
    text: "[1.x]",
    error:
      "'x' is invalid within a number, immediately after a decimal point ('.'). Expected a digit ('0'-'9').",
  },
  {
    text: "[1ex]",
    error:
      "'x' is invalid within a number, immediately after a sign character ('+' or '-'). Expected a digit ('0'-'9').",
  },
  { text: "[1.0x]", error: "'x' is an invalid end of a number. Expected 'E' or 'e'." },
  { text: "[1e5x]", error: "'x' is an invalid end of a number. Expected a delimiter." },
  { text: "[0x]", error: "'x' is an invalid end of a number. Expected a delimiter." },
  {
    text: "[0.]",
    error:
      "']' is invalid within a number, immediately after a decimal point ('.'). Expected a digit ('0'-'9').",
  },
  { text: "[00]", error: "Invalid leading zero before '0'." },
  { text: "[1E5]" },
  { text: "[1e05]" },
  { text: "[-0.0e-0]" },
  { text: '"\\uD800x"' },
  { text: '"\\ud83d\\ude00"' },
  { text: '{"a":1}{', error: "'{' is invalid after a single JSON value. Expected end of data." },
  {
    text: "[1]\u0000",
    error: "'0x00' is invalid after a single JSON value. Expected end of data.",
  },
  {
    text: '"a\u001fb"',
    error: "'0x1F' is invalid within a JSON string. The string should be correctly escaped.",
  },
  { text: "[nulxyz]", error: "'nulxyz]' is an invalid JSON literal. Expected the literal 'null'." },
  { text: "[txyzwv]", error: "'txyzwv]' is an invalid JSON literal. Expected the literal 'true'." },
  {
    text: "[fxyzwvu]",
    error: "'fxyzwvu]' is an invalid JSON literal. Expected the literal 'false'.",
  },
  { text: "[fal]", error: "'fal]' is an invalid JSON literal. Expected the literal 'false'." },
  { text: "[falsx]", error: "'falsx]' is an invalid JSON literal. Expected the literal 'false'." },
  { text: "nulxyz", error: "'nulxyz' is an invalid JSON literal. Expected the literal 'null'." },
  { text: "txyzwvq", error: "'txyzwvq' is an invalid JSON literal. Expected the literal 'true'." },
  {
    text: "fxyzwvuq",
    error: "'fxyzwvuq' is an invalid JSON literal. Expected the literal 'false'.",
  },
  { text: "t", error: "'t' is an invalid JSON literal. Expected the literal 'true'." },
  { text: "f", error: "'f' is an invalid JSON literal. Expected the literal 'false'." },
  { text: "n", error: "'n' is an invalid JSON literal. Expected the literal 'null'." },
  { text: "[t", error: "'t' is an invalid JSON literal. Expected the literal 'true'." },
  { text: "[f", error: "'f' is an invalid JSON literal. Expected the literal 'false'." },
  { text: "[n", error: "'n' is an invalid JSON literal. Expected the literal 'null'." },
  { text: "[tr", error: "'tr' is an invalid JSON literal. Expected the literal 'true'." },
  { text: "[ture]", error: "'ture]' is an invalid JSON literal. Expected the literal 'true'." },
  { text: "[1/]", error: "'/' is invalid after a value. Expected either ',', '}', or ']'." },
  { text: "1/", error: "'/' is invalid after a single JSON value. Expected end of data." },
  { text: '[1"a"]', error: "'\"' is an invalid end of a number. Expected a delimiter." },
  { text: "[1{]", error: "'{' is an invalid end of a number. Expected a delimiter." },
  { text: "[1[]", error: "'[' is an invalid end of a number. Expected a delimiter." },
  { text: "[1]", error: "'0x7F' is an invalid end of a number. Expected a delimiter." },
  { text: "[]", error: "'0x7F' is an invalid start of a value." },
  { text: "[1\t]" },
  { text: '{"a":1/}', error: "'/' is invalid after a value. Expected either ',', '}', or ']'." },
  { text: '["a"/]', error: "'/' is invalid after a value. Expected either ',', '}', or ']'." },
  { text: "[true/]", error: "'/' is invalid after a value. Expected either ',', '}', or ']'." },
  { text: "[1,/]", error: "'/' is an invalid start of a value." },
  { text: '"a" "b"', error: "'\"' is invalid after a single JSON value. Expected end of data." },
  { text: "[1]/", error: "'/' is invalid after a single JSON value. Expected end of data." },
  { text: "[-0]" },
  { text: "[-01]", error: "Invalid leading zero before '1'." },
  {
    text: "[1.5e]",
    error:
      "']' is invalid within a number, immediately after a sign character ('+' or '-'). Expected a digit ('0'-'9').",
  },
  {
    text: "[1.5e-]",
    error:
      "']' is invalid within a number, immediately after a sign character ('+' or '-'). Expected a digit ('0'-'9').",
  },
  { text: "[1.5E+5]" },
  { text: "[1.5e5.5]", error: "'.' is an invalid end of a number. Expected a delimiter." },
  { text: "[1.5e5e]", error: "'e' is an invalid end of a number. Expected a delimiter." },
  { text: "[1e5.]", error: "'.' is an invalid end of a number. Expected a delimiter." },
  {
    text: "[0e]",
    error:
      "']' is invalid within a number, immediately after a sign character ('+' or '-'). Expected a digit ('0'-'9').",
  },
  { text: "[0.0.0]", error: "'.' is an invalid end of a number. Expected 'E' or 'e'." },
  { text: "[0e1]" },
  {
    text: "[-.5]",
    error:
      "'.' is invalid within a number, immediately after a sign character ('+' or '-'). Expected a digit ('0'-'9').",
  },
  {
    text: "[-e]",
    error:
      "'e' is invalid within a number, immediately after a sign character ('+' or '-'). Expected a digit ('0'-'9').",
  },
  { text: "[.5]", error: "'.' is an invalid start of a value." },
  { text: "[1\n2]", error: "'2' is invalid after a value. Expected either ',', '}', or ']'." },
  { text: '{"a":1"b":2}', error: "'\"' is an invalid end of a number. Expected a delimiter." },
  {
    text: '{"a":"b""c":2}',
    error: "'\"' is invalid after a value. Expected either ',', '}', or ']'.",
  },
  {
    text: '{"a":[]"b":2}',
    error: "'\"' is invalid after a value. Expected either ',', '}', or ']'.",
  },
  { text: "[[]1]", error: "'1' is invalid after a value. Expected either ',', '}', or ']'." },
  { text: "[{}1]", error: "'1' is invalid after a value. Expected either ',', '}', or ']'." },
  {
    text: '{"a":{}"b"}',
    error: "'\"' is invalid after a value. Expected either ',', '}', or ']'.",
  },
  { text: '["\\u0000"]' },
  {
    text: '["\\U0041"]',
    error:
      "'U' is an invalid escapable character within a JSON string. The string should be correctly escaped.",
  },
  {
    text: '["\\a"]',
    error:
      "'a' is an invalid escapable character within a JSON string. The string should be correctly escaped.",
  },
  {
    text: '["\\\'"]',
    error:
      "''' is an invalid escapable character within a JSON string. The string should be correctly escaped.",
  },
  {
    text: '["\\é"]',
    error:
      "'0xC3' is an invalid escapable character within a JSON string. The string should be correctly escaped.",
  },
  {
    text: '["\\\u0001"]',
    error:
      "'0x01' is an invalid escapable character within a JSON string. The string should be correctly escaped.",
  },
  {
    text: '["\\u"]',
    error:
      "'\"' is not a hex digit following '\\u' within a JSON string. The string should be correctly escaped.",
  },
  { text: '["\\uDFFF\\uD800"]' },
  { text: '{"a" ', error: "Expected a value, but instead reached end of data." },
  {
    text: '{"a": ',
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  {
    text: '"\\x',
    error:
      "'x' is an invalid escapable character within a JSON string. The string should be correctly escaped.",
  },
  {
    text: '"a\u0001',
    error: "'0x01' is invalid within a JSON string. The string should be correctly escaped.",
  },
  {
    text: '"\\u12G',
    error:
      "'G' is not a hex digit following '\\u' within a JSON string. The string should be correctly escaped.",
  },
  { text: '"\\u', error: "Expected end of string, but instead reached end of data." },
  {
    text: '["\\x',
    error:
      "'x' is an invalid escapable character within a JSON string. The string should be correctly escaped.",
  },
  {
    text: "[1,2 ",
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  { text: '{"a":1}x', error: "'x' is invalid after a single JSON value. Expected end of data." },
  { text: '{"a":1} x', error: "'x' is invalid after a single JSON value. Expected end of data." },
  {
    text: "[[1]",
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  {
    text: '{"a":{}',
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  {
    text: '[{"a":1}',
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  {
    text: '{"a":[1]',
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  { text: '" ', error: "Expected end of string, but instead reached end of data." },
  {
    text: '["a"\u0000]',
    error: "'0x00' is invalid after a value. Expected either ',', '}', or ']'.",
  },
  { text: '{"a":1,"b"', error: "Expected a value, but instead reached end of data." },
  {
    text: '{"a":1,"b":',
    error:
      "Expected depth to be zero at the end of the JSON payload. There is an open JSON object or array that should be closed.",
  },
  { text: "[-1.", error: "Expected a digit ('0'-'9'), but instead reached end of data." },
  { text: "[0.5e", error: "Expected a digit ('0'-'9'), but instead reached end of data." },
  {
    text: '{"\\x":1}',
    error:
      "'x' is an invalid escapable character within a JSON string. The string should be correctly escaped.",
  },
  {
    text: '{"a\u0001":1}',
    error: "'0x01' is invalid within a JSON string. The string should be correctly escaped.",
  },
  {
    text: '{"a":"\\u00"}',
    error:
      "'\"' is not a hex digit following '\\u' within a JSON string. The string should be correctly escaped.",
  },
  {
    text: '["\\uZZZZ"]',
    error:
      "'Z' is not a hex digit following '\\u' within a JSON string. The string should be correctly escaped.",
  },
  {
    text: '"\\uD83D\\uDE00\\u12"',
    error:
      "'\"' is not a hex digit following '\\u' within a JSON string. The string should be correctly escaped.",
  },
  { text: "[1,é]", error: "'0xC3' is an invalid start of a value." },
  { text: "[1\r\n,2]" },
  { text: '[1,2,"中"]' },
  {
    text: '{"中":1,}',
    error:
      "The JSON object contains a trailing comma at the end which is not supported in this mode. Change the reader options.",
  },
  { text: "中", error: "'0xE4' is an invalid start of a value." },
  { text: "[😀]", error: "'0xF0' is an invalid start of a value." },
  {
    text: '"\\😀"',
    error:
      "'0xF0' is an invalid escapable character within a JSON string. The string should be correctly escaped.",
  },
  { text: "[ÿ]", error: "'0xC3' is an invalid start of a value." },
  { text: "", error: "'0xC2' is an invalid start of a value." },
];

/** `read` for a text read, or the error it throws. */
export function textOutcomeOf(read: () => unknown): string {
  try {
    read();

    return "read";
  } catch (error) {
    const { name, message } = error as Error;

    return `${name}: ${message}`;
  }
}

/** What reading the row must give: `read`, or STJ's error. */
export function expectedTextOutcomeOf(row: JsonTextRow): string {
  return row.error ? `JsonException: ${row.error}` : "read";
}
