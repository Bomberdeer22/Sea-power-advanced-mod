"""
Read a .NET assembly: types, members, signatures, method bodies.

Nothing here loads or runs the assembly, and no game DLLs are needed beyond the file itself. That
matters for a Unity game: reflection-based tools would have to resolve UnityEngine, Noesis, UniRx
and the rest just to list a type. Metadata plus IL is enough to answer "does this member exist, what
is its signature, and what does it call".

Requires: dnfile (metadata) and dncil (IL decoding). See README.md.
"""

from __future__ import annotations

import logging
import struct

import dnfile
from dncil.cil.body import CilMethodBody
from dncil.cil.body.reader import CilMethodBodyReaderBase
from dnfile.stream import HeapItemString

# dnfile logs a warning for metadata it cannot read (a blob with an invalid length prefix, say).
# Those are informational: this reader degrades to "?" for the one signature affected, and the
# warnings would otherwise interleave with the survey output.
for _logger_name in ("dnfile", "dnfile.utils", "dnfile.stream"):
    logging.getLogger(_logger_name).setLevel(logging.ERROR)

PRIMITIVES = {
    0x01: "void", 0x02: "bool", 0x03: "char", 0x04: "sbyte", 0x05: "byte",
    0x06: "short", 0x07: "ushort", 0x08: "int", 0x09: "uint", 0x0A: "long",
    0x0B: "ulong", 0x0C: "float", 0x0D: "double", 0x0E: "string",
    0x16: "TypedReference", 0x18: "IntPtr", 0x19: "UIntPtr", 0x1C: "object",
}


# ----------------------------------------------------------------------------- dnfile helpers

def text(value) -> str:
    """dnfile stores names as heap items; a plain string is also acceptable."""
    if isinstance(value, HeapItemString):
        return value.value or ""
    return str(value or "")


class _SigReader:
    """Cursor over a signature blob."""

    def __init__(self, data: bytes):
        self.data, self.i = data, 0

    def u8(self) -> int:
        v = self.data[self.i]
        self.i += 1
        return v

    def compressed(self) -> int:
        first = self.u8()
        if first & 0x80 == 0:
            return first
        if first & 0xC0 == 0x80:
            return ((first & 0x3F) << 8) | self.u8()
        return ((first & 0x1F) << 24) | (self.u8() << 16) | (self.u8() << 8) | self.u8()


def access(flags, prefix: str) -> str:
    """Programming-language access level from field (`fd`) or method (`md`) flags."""
    # dnfile spells the "assembly" (internal) flag differently per table: fdAssembly for fields,
    # mdAssem for methods.
    for name, label in (
        ("Public", "public"), ("Family", "protected"), ("FamORAssem", "protected internal"),
        ("Assembly", "internal"), ("Assem", "internal"),
        ("FamANDAssem", "private protected"), ("Private", "private"),
    ):
        if getattr(flags, prefix + name, False):
            return label
    return "?"


def modifiers(flags, prefix: str) -> str:
    """The bits that change how you may call something."""
    out = []
    for name in ("Static", "Abstract", "Virtual", "NewSlot", "Sealed", "InitOnly", "Literal"):
        if getattr(flags, prefix + name, False):
            out.append(name.lower())
    return ("[" + ",".join(out) + "]") if out else ""


# ----------------------------------------------------------------------------- the assembly

class Assembly:
    def __init__(self, path: str):
        self.path = path
        self.pe = dnfile.dnPE(path)
        self.md = self.pe.net.mdtables

        self.types: dict[str, object] = {}
        self._type_index: dict[int, str] = {}
        # TypeDef rows carry no row id, so the index comes from position (RIDs are 1-based).
        for index, row in enumerate(self.md.TypeDef.rows, start=1):
            namespace, name = text(row.TypeNamespace), text(row.TypeName)
            full = (namespace + "." if namespace else "") + name
            self.types[full] = row
            self._type_index[index] = full

        self._declaring: dict[tuple[str, int], str] = {}
        for index, row in enumerate(self.md.TypeDef.rows, start=1):
            full = self._type_index.get(index, "?")
            for entry in (row.MethodList or []):
                self._declaring[("m", entry.row_index)] = full
            for entry in (row.FieldList or []):
                self._declaring[("f", entry.row_index)] = full

        self._nested = self._build_nested()

        self._member_labels: dict[int, str] = {}
        self._build_member_labels()

    def table(self, name: str) -> list:
        """Rows of a metadata table; dnfile reports an absent table as None."""
        table = getattr(self.md, name, None)
        return list(table.rows) if table is not None else []

    # -- identity -----------------------------------------------------------------------

    @property
    def name(self) -> str:
        try:
            return text(self.table("Assembly")[0].Name)
        except Exception:
            return "?"

    @property
    def version(self) -> str:
        try:
            row = self.table("Assembly")[0]
            parts = [row.MajorVersion, row.MinorVersion, row.BuildNumber, row.RevisionNumber]
            return ".".join(str(p) for p in parts)
        except Exception:
            return "?"

    @property
    def type_name_without_namespace(self) -> dict[str, list[str]]:
        """Short type name -> full names, for 'find the type called X'."""
        out: dict[str, list[str]] = {}
        for full in self.types:
            out.setdefault(full.rsplit(".", 1)[-1], []).append(full)
        return out

    def summary(self) -> dict:
        return {
            "path": self.path,
            "assembly": self.name,
            "version": self.version,
            "types": len(self.types),
            "methods": len(self.table("MethodDef")),
            "fields": len(self.table("Field")),
            "assembly_refs": len(self.table("AssemblyRef")),
        }

    def assembly_references(self) -> list[str]:
        return [text(row.Name) for row in self.table("AssemblyRef")]

    # -- type relationships -------------------------------------------------------------

    def _build_nested(self) -> dict[int, str]:
        out = {}
        for row in self.table("NestedClass"):
            try:
                outer = self.md.TypeDef.rows[row.EnclosingClass.row_index - 1]
                namespace, name = text(outer.TypeNamespace), text(outer.TypeName)
                out[row.NestedClass.row_index] = (namespace + "." if namespace else "") + name
            except Exception:
                pass
        return out

    def enum_values(self, full_name: str) -> list[tuple[str, int]]:
        """(name, value) pairs of an enum, read from the Constant table."""
        typedef = self.types.get(full_name)
        if typedef is None:
            return []

        values = {}
        for row in self.table("Constant"):
            try:
                parent = row.Parent
                if parent.table.name != "Field":
                    continue
                raw = row.Value.value if hasattr(row.Value, "value") else row.Value
                values[parent.row_index] = struct.unpack("<i", bytes(raw)[:4])[0]
            except Exception:
                continue

        out = []
        for entry in (typedef.FieldList or []):
            field_row = self.table("Field")[entry.row_index - 1]
            if entry.row_index in values:
                out.append((text(field_row.Name), values[entry.row_index]))
        return out

    # -- members ------------------------------------------------------------------------

    def fields(self, typedef) -> list[tuple[str, str, object]]:
        """(name, type, flags) for each field."""
        out = []
        for entry in (typedef.FieldList or []):
            row = self.table("Field")[entry.row_index - 1]
            reader = _SigReader(bytes(row.Signature.value))
            reader.u8()  # FIELD signature marker
            try:
                type_name = self._sig_type(reader)
            except Exception:
                type_name = "?"
            out.append((text(row.Name), type_name, row.Flags))
        return out

    def methods(self, typedef) -> list[dict]:
        out = []
        for entry in (typedef.MethodList or []):
            row = self.table("MethodDef")[entry.row_index - 1]
            try:
                returns, parameters = self._method_signature(row)
            except Exception:
                returns, parameters = "?", []
            out.append({
                "name": text(row.Name),
                "returns": returns,
                "parameters": parameters,
                "flags": row.Flags,
                "row": row,
                "rva": getattr(row, "Rva", 0) or 0,
            })
        return out

    def method(self, type_name: str, method_name: str, parameters: int | None = None) -> dict | None:
        typedef = self.types.get(type_name) or self.types.get(self._resolve_short(type_name))
        if typedef is None:
            return None
        for candidate in self.methods(typedef):
            if candidate["name"] != method_name:
                continue
            if parameters is not None and len(candidate["parameters"]) != parameters:
                continue
            return candidate
        return None

    def _resolve_short(self, name: str) -> str:
        options = self.type_name_without_namespace.get(name) or []
        return options[0] if len(options) == 1 else name

    # -- signatures ---------------------------------------------------------------------

    def _method_signature(self, row) -> tuple[str, list[str]]:
        reader = _SigReader(bytes(row.Signature.value))
        convention = reader.u8()

        # ECMA-335 II.23.2.1: a GENERIC method signature carries its own generic parameter count
        # between the calling convention and the parameter count. Skipping it shifts every later
        # field by one, which turns a return type into "void" and looks plausible enough to miss.
        if convention & 0x10:
            reader.compressed()

        count = reader.compressed()
        returns = self._sig_type(reader)
        return returns, [self._sig_type(reader) for _ in range(count)]

    def _sig_type(self, reader: _SigReader) -> str:
        element = reader.u8()

        if element in PRIMITIVES:
            return PRIMITIVES[element]
        if element == 0x0F:
            return self._sig_type(reader) + "*"
        if element == 0x10:
            return "ref " + self._sig_type(reader)
        if element in (0x11, 0x12):
            return self._type_name(reader.compressed())
        if element == 0x1D:
            return self._sig_type(reader) + "[]"
        if element == 0x13:
            return "!" + str(reader.compressed())
        if element == 0x1E:
            return "!!" + str(reader.compressed())
        if element == 0x15:  # generic instantiation
            outer = self._sig_type(reader)
            count = reader.compressed()
            return outer + "<" + ", ".join(self._sig_type(reader) for _ in range(count)) + ">"
        if element == 0x14:  # array
            inner = self._sig_type(reader)
            rank = reader.compressed()
            reader.compressed()
            reader.compressed()
            for _ in range(reader.compressed()):
                reader.compressed()
            return inner + "[" + "," * (rank - 1) + "]"
        if element in (0x1F, 0x20):  # custom modifier
            self._type_name(reader.compressed())
            return self._sig_type(reader)
        if element == 0x45:  # pinned
            return self._sig_type(reader)
        return "?"

    def _type_name(self, coded: int) -> str:
        tag, index = coded & 3, coded >> 2
        try:
            if tag == 0:
                row = self.md.TypeDef.rows[index - 1]
            elif tag == 1:
                row = self.table("TypeRef")[index - 1]
            else:
                return "?"
            namespace, name = text(row.TypeNamespace), text(row.TypeName)
            return (namespace + "." if namespace else "") + name
        except Exception:
            return "?"

    def type_name_of_token(self, token: int) -> str:
        table = token >> 24
        index = token & 0xFFFFFF
        try:
            if table == 0x02:
                row = self.md.TypeDef.rows[index - 1]
            elif table == 0x01:
                row = self.table("TypeRef")[index - 1]
            elif table == 0x1B:
                row = self.table("TypeSpec")[index - 1]
                return self._sig_type(_SigReader(bytes(row.Signature.value)))
            else:
                return f"token(0x{token:08x})"
            namespace, name = text(row.TypeNamespace), text(row.TypeName)
            return (namespace + "." if namespace else "") + name
        except Exception:
            return f"token(0x{token:08x})"

    # -- member labels ------------------------------------------------------------------

    def _build_member_labels(self):
        for index, row in enumerate(self.table("Field"), start=1):
            declaring = self._declaring.get(("f", index), "?")
            self._member_labels[0x04000000 | index] = declaring + "::" + text(row.Name)

        for index, row in enumerate(self.table("MethodDef"), start=1):
            declaring = self._declaring.get(("m", index), "?")
            self._member_labels[0x06000000 | index] = declaring + "::" + text(row.Name)

        for index, row in enumerate(self.table("MemberRef"), start=1):
            try:
                reference = row.Class
                table = reference.table.name
                if table == "TypeRef":
                    owner = self.table("TypeRef")[reference.row_index - 1]
                    namespace, name = text(owner.TypeNamespace), text(owner.TypeName)
                    declaring = (namespace + "." if namespace else "") + name
                elif table == "TypeDef":
                    declaring = self._type_index.get(reference.row_index, "?")
                elif table == "TypeSpec":
                    spec = self.table("TypeSpec")[reference.row_index - 1]
                    declaring = self._sig_type(_SigReader(bytes(spec.Signature.value)))
                else:
                    declaring = table
            except Exception:
                declaring = "?"
            self._member_labels.setdefault(0x0A000000 | index, declaring + "::" + text(row.Name))

    def user_string(self, token: int) -> str:
        try:
            value = self.pe.net.user_strings.get(token & 0xFFFFFF)
            return value.value if hasattr(value, "value") else str(value)
        except Exception:
            return "?"

    def label(self, token: int) -> str:
        """Human-readable form of a metadata token used by an instruction."""
        if token == 0:
            return "null"

        table = token >> 24
        if table == 0x70:
            return repr(self.user_string(token))
        if token in self._member_labels:
            return self._member_labels[token]
        if table in (0x01, 0x02, 0x1B):
            return self.type_name_of_token(token)
        if table == 0x11:
            return f"signature(0x{token:08x})"
        return f"token(0x{token:08x})"

    # -- method bodies ------------------------------------------------------------------

    def body_bytes(self, rva: int) -> bytes:
        """The raw IL bytes of a method body, sized from its own header.

        Reading the header directly (ECMA-335 II.25.4) is a dozen lines and makes the cheap
        "does this body mention token X at all?" prefilter below exact.
        """
        try:
            offset = self.pe.get_offset_from_rva(rva)
            data = self.pe.__data__
            first = data[offset]

            if first & 0x03 == 0x02:  # tiny header: size in the top six bits
                size = first >> 2
                return bytes(data[offset + 1:offset + 1 + size])

            if first & 0x03 == 0x03:  # fat header: flags/size, then maxstack, code size, locals
                flags_and_size = struct.unpack_from("<H", data, offset)[0]
                code_size = struct.unpack_from("<I", data, offset + 4)[0]
                start = offset + ((flags_and_size >> 12) * 4)
                return bytes(data[start:start + code_size])
        except Exception:
            pass
        return b""

    def tokens_for(self, needles: list[str]) -> set[int]:
        """Member tokens whose label contains any of `needles`."""
        return {token for token, label in self._member_labels.items()
                if any(needle in label for needle in needles)}

    def instructions(self, method: dict) -> list[tuple[int, str, object]]:
        """(offset, opcode, labelled operand) for a method with an IL body."""
        rva = method.get("rva")
        if not rva:
            return []

        body = CilMethodBody(_BodyReader(self.pe, rva))
        out = []
        base = body.instructions[0].offset if body.instructions else 0
        for instruction in body.instructions:
            operand = instruction.operand
            operand = getattr(operand, "value", operand)

            if isinstance(operand, list):
                operand = [getattr(item, "value", item) for item in operand]
                operand = [self.label(item) if isinstance(item, int) and item >> 24 else item
                           for item in operand]
            elif isinstance(operand, int) and operand >> 24 in (0x01, 0x02, 0x04, 0x06, 0x0A, 0x11, 0x1B, 0x70):
                operand = self.label(operand)

            # Report method-relative offsets: they match IL_0000-style listings regardless of where
            # the body sits in the file, which is what makes two builds comparable.
            out.append((instruction.offset - base, instruction.opcode.name, operand))
        return out

    def disassembly(self, method: dict) -> str:
        """An ILDasm-like listing, for reading and for the self-test."""
        lines = []
        for offset, opcode, operand in self.instructions(method):
            text_operand = "" if operand is None else f" {operand}"
            lines.append(f"IL_{offset:04x}: {opcode}{text_operand}")
        return "\n".join(lines)

    def calls(self, method: dict) -> list[str]:
        """Every member token a method references, in order."""
        out = []
        for _, opcode, operand in self.instructions(method):
            if opcode in ("call", "callvirt", "newobj", "ldftn", "ldsfld", "ldfld", "stsfld", "stfld", "ldtoken") \
                    and isinstance(operand, str):
                out.append(operand)
        return out

    def iter_call_sites(self, progress=None, needles: list[str] | None = None):
        """Yield (declaring type, method, referenced member) for every body in the assembly.

        Works from the raw MethodDef rows rather than `methods()` on each type: only the name and
        the body RVA are needed here, and parsing a signature for every method in a large assembly
        costs more than decoding the bodies does.

        One pass over every body is still expensive, so callers that need several patterns should
        use this rather than scanning once per pattern. `progress(seen, total)` is called
        periodically.
        """
        rows = self.table("MethodDef")
        total = len(rows)

        # A call instruction stores its member token verbatim, so a body that does not contain any
        # of the wanted tokens cannot contain a matching call site. Skipping those bodies by a byte
        # search avoids decoding most of a large assembly.
        wanted = [struct.pack("<I", token) for token in self.tokens_for(needles or [])] if needles else []

        for index, row in enumerate(rows, start=1):
            rva = getattr(row, "Rva", 0) or 0
            if not rva:
                continue

            if progress and index % 5000 == 0:
                progress(index, total)

            body = self.body_bytes(rva)
            if wanted and not any(token in body for token in wanted):
                continue

            method = {
                "name": text(row.Name),
                "rva": rva,
                "flags": row.Flags,
                "returns": "?",
                "parameters": [],
                "row": row,
            }
            try:
                targets = self.calls(method)
            except Exception:
                # A body this reader cannot decode must not abort the whole survey.
                continue

            declaring = self._declaring.get(("m", index), "?")
            for target in targets:
                yield declaring, method["name"], target

    def find_calls(self, needle: str, types: list[str] | None = None) -> list[tuple[str, str, str]]:
        """(declaring type, method, referenced member) for every call site matching `needle`."""
        out = []
        for full_name, method_name, target in self.iter_call_sites(needles=[needle]):
            if needle in target and (not types or any(p in full_name for p in types)):
                out.append((full_name, method_name, target))
        return out


class _BodyReader(CilMethodBodyReaderBase):
    """dncil needs random access into the file at an RVA."""

    def __init__(self, pe, rva: int):
        self.pe = pe
        self.offset = pe.get_offset_from_rva(rva)

    def read(self, count: int) -> bytes:
        data = self.pe.__data__[self.offset:self.offset + count]
        self.offset += count
        return data

    def tell(self) -> int:
        return self.offset

    def seek(self, offset: int) -> None:
        self.offset = offset
