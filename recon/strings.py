import re, sys
def strings(data, minlen=4):
    out = []
    # ASCII
    cur = bytearray()
    for b in data:
        if 32 <= b < 127:
            cur.append(b)
        else:
            if len(cur) >= minlen:
                out.append(cur.decode('ascii'))
            cur = bytearray()
    if len(cur) >= minlen:
        out.append(cur.decode('ascii'))
    return out
def utf16(data, minlen=4):
    out = []
    cur = []
    i = 0
    while i < len(data) - 1:
        lo, hi = data[i], data[i+1]
        if hi == 0 and 32 <= lo < 127:
            cur.append(chr(lo)); i += 2
        else:
            if len(cur) >= minlen:
                out.append(''.join(cur))
            cur = []; i += 1
    if len(cur) >= minlen:
        out.append(''.join(cur))
    return out

path = sys.argv[1]
data = open(path, 'rb').read()
s = strings(data) + utf16(data)
kw = sys.argv[2] if len(sys.argv) > 2 else None
if kw:
    pat = re.compile(kw, re.I)
    seen = set()
    for x in s:
        if pat.search(x) and x not in seen:
            seen.add(x)
            print(x)
else:
    print('\n'.join(s))
