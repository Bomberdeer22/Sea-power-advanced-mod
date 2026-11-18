#!/usr/bin/env python3
"""Read Sea Power's types and members straight out of Seapower-Scripts.dll.

No game, no compiler, no Windows: a .NET assembly stores every type, field, method, parameter name
and optional-parameter flag in its metadata tables.

    python3 tools/read-metadata.py "Sea Power_Data/Managed/Seapower-Scripts.dll" WeaponContainer
    python3 tools/read-metadata.py Seapower-Scripts.dll hatch --members
    python3 tools/read-metadata.py Seapower-Scripts.dll . --members     # every name containing "."

Needs: pip install dnfile
"""
import re
import sys

try:
    import dnfile
    from dnfile.stream import HeapItemString
except ImportError:
    sys.exit("This needs the dnfile package:  pip install dnfile")

PRIMITIVES = {
    0x01: "void", 0x02: "bool", 0x03: "char", 0x04: "sbyte", 0x05: "byte", 0x06: "short",
    0x07: "ushort", 0x08: "int", 0x09: "uint", 0x0a: "long", 0x0b: "ulong", 0x0c: "float",
    0x0d: "double", 0x0e: "string", 0x16: "TypedReference", 0x18: "IntPtr", 0x19: "UIntPtr",
    0x1c: "object",
}


def text(item):
    return (item.value or "") if isinstance(item, HeapItemString) else str(item or "")


class Reader:
    def __init__(self, data):
        self.data, self.i = data, 0

    def u8(self):
        value = self.data[self.i]
        self.i += 1
        return value

    def u(self):
        first = self.u8()
        if first & 0x80 == 0:
            return first
        if first & 0xC0 == 0x80:
            return ((first & 0x3F) << 8) | self.u8()
        return ((first & 0x1F) << 24) | (self.u8() << 16) | (self.u8() << 8) | self.u8()


class Metadata:
    def __init__(self, path):
        self.pe = dnfile.dnPE(path)
        self.md = self.pe.net.mdtables
        self.types = {}
        for row in self.md.TypeDef.rows:
            namespace, name = text(row.TypeNamespace), text(row.TypeName)
            self.types[(namespace + "." if namespace else "") + name] = row

    def type_name(self, coded):
        tag, index = coded & 0x3, coded >> 2
        try:
            if tag == 0:
                row = self.md.TypeDef.rows[index - 1]
                namespace, name = text(row.TypeNamespace), text(row.TypeName)
                return (namespace + "." if namespace else "") + name
            if tag == 1:
                row = self.md.TypeRef.rows[index - 1]
                namespace, name = text(row.TypeNamespace), text(row.TypeName)
                return (namespace + "." if namespace else "") + name
        except Exception:
            pass
        return "?"

    def sig_type(self, reader):
        element = reader.u8()
        if element in PRIMITIVES:
            return PRIMITIVES[element]
        if element == 0x0F:
            return self.sig_type(reader) + "*"
        if element == 0x10:
            return "ref " + self.sig_type(reader)
        if element in (0x11, 0x12):
            return self.type_name(reader.u())
        if element == 0x1D:
            return self.sig_type(reader) + "[]"
        if element == 0x13:
            return "!" + str(reader.u())
        if element == 0x1E:
            return "!!" + str(reader.u())
        if element == 0x15:
            outer = self.sig_type(reader)
            args = [self.sig_type(reader) for _ in range(reader.u())]
            return outer + "<" + ", ".join(args) + ">"
        if element == 0x14:
            inner = self.sig_type(reader)
            rank = reader.u()
            return inner + "[" + "," * (rank - 1) + "]"
        if element in (0x1F, 0x20):
            self.type_name(reader.u())
            return self.sig_type(reader)
        if element == 0x45:
            return self.sig_type(reader)
        return "?"

    def method_signature(self, row):
        reader = Reader(row.Signature.value)
        reader.u8()
        count = reader.u()
        returns = self.sig_type(reader)
        return returns, [self.sig_type(reader) for _ in range(count)]

    def describe(self, full_name):
        row = self.types.get(full_name)
        if row is None:
            return False
        print("TYPE", full_name)
        for index in (row.FieldList or []):
            field = index.row
            reader = Reader(field.Signature.value)
            reader.u8()
            public = getattr(field.Flags, "fdPublic", False)
            print(f"   {'public ' if public else '       '}field  {self.sig_type(reader)} {text(field.Name)}")
        for index in (row.MethodList or []):
            method = index.row
            name = text(method.Name)
            if name.startswith("<"):
                continue
            returns, parameters = self.method_signature(method)
            names = []
            for pindex in (method.ParamList or []):
                parameter = pindex.row
                optional = getattr(parameter.Flags, "pdOptional", False)
                names.append(text(parameter.Name) + ("=opt" if optional else ""))
            print(f"   method {returns} {name}({', '.join(names)})")
        print()
        return True

    def search_members(self, pattern):
        regex = re.compile(pattern, re.I)
        for full_name in sorted(self.types):
            row = self.types[full_name]
            hits = [f"field  {text(i.row.Name)}" for i in (row.FieldList or []) if regex.search(text(i.row.Name))]
            hits += [f"method {text(i.row.Name)}" for i in (row.MethodList or []) if regex.search(text(i.row.Name))]
            if hits:
                print("TYPE", full_name)
                for hit in hits:
                    print("   " + hit)
                print()


def main():
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    path, pattern = sys.argv[1], sys.argv[2]
    members_only = "--members" in sys.argv[3:]
    api = Metadata(path)

    if members_only:
        api.search_members(pattern)
    elif not api.describe(pattern):
        regex = re.compile(pattern, re.I)
        for full_name in sorted(name for name in api.types if regex.search(name)):
            api.describe(full_name)


if __name__ == "__main__":
    main()
