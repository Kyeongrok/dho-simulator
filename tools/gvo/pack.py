"""0001\*.bin 묶음 읽개.

짜임 두 가지:
  큰 묶음  : u8 0xFF, u16 개수, u32 자리 × (개수+1)   (sh/md/hm/fi/ef)
  작은 묶음: u8 개수,           u32 자리 × (개수+1)   (em/sa/fm)
자리는 파일 처음부터. 마지막 자리 = 파일 크기. 크기 0 인 항목은 빈 칸.
"""
import struct, sys, os, mmap
import gvo


class Pack:
    def __init__(self, rel):
        self.path = gvo.game_path(rel) if not os.path.isabs(rel) else rel
        self.f = open(self.path, "rb")
        self.size = os.path.getsize(self.path)
        self.mm = mmap.mmap(self.f.fileno(), 0, access=mmap.ACCESS_READ)
        b0 = self.mm[0]
        if b0 == 0xFF:
            self.count = struct.unpack_from("<H", self.mm, 1)[0]
            base = 3
        else:
            self.count = b0
            base = 1
        self.offsets = list(struct.unpack_from("<%dI" % (self.count + 1), self.mm, base))

    def entry(self, i):
        return self.mm[self.offsets[i]:self.offsets[i + 1]]

    def __len__(self):
        return self.count


if __name__ == "__main__":
    p = Pack(sys.argv[1])
    print(p.count, "entries", "ok" if p.offsets[-1] == p.size else "END MISMATCH %d %d" % (p.offsets[-1], p.size))
    lim = int(sys.argv[2]) if len(sys.argv) > 2 else 40
    for i in range(min(p.count, lim)):
        e = p.entry(i)
        print("%4d off=%9d size=%8d %s | %s" % (i, p.offsets[i], len(e), e[:32].hex(" "),
              "".join(chr(c) if 32 <= c < 127 else "." for c in e[:32])))
