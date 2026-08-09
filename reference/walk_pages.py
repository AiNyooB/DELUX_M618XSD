# -*- coding: utf-8 -*-
"""
整页走查：打开设备 → 0x0C 唤醒 → 连续读取 N 个 64 字节内存页 → 关闭。

用法:
  python walk_pages.py <标签>           # 读取 64 页并保存
  python walk_pages.py <标签> --pages 96  # 自定义页数
  python walk_pages.py compare <a> <b>   # 对比两个快照

扩展的 walk_pages.py：读取更多页，标注已知区域，支持对比。
"""
import ctypes
import os
import sys
import time

APP = r"C:\Users\fresh\Downloads\618XSD\extracted\app"
DLL_PATH = os.path.join(APP, "hiddriver_ms_4.dll")
VID, PID = 0x1D57, 0xFA60
SNAPSHOT_DIR = r"C:\Users\fresh\Downloads\618XSD\snapshots"

# ── 已知页标注（根据现有逆向知识） ──
PAGE_ANNOTATIONS = {
    0:  "P00: 最后命令回显 [0..9] + 0x04报告 [10..63]",
    1:  "P01: 配置数据",
    2:  "P02: 配置数据",
    3:  "P03: 配置数据",
    4:  "P04: 配置数据",
    5:  "P05: 配置数据",
    6:  "P06: 配置数据",
    7:  "P07: 配置数据",
    8:  "P08: 配置数据",
    9:  "P09: 配置数据",
    10: "P10: 配置数据",
    33: "P33: ⚠️ 疑似按钮配置（宏ID4=0x16, 宏ID1=0x08, 新宏=0x2d）",
}

# 已知关键字节偏移（页面内偏移 0-63）
KNOWN_OFFSETS = {
    # 在 P00 中
    "P00.[0]":     "Report ID（最后命令回显）",
    "P00.[1..2]":  "命令字",
    "P00.[5]":     "DPI 档位启用位图",
    "P00.[24]":    "当前活跃 DPI 档位索引",
    "P00.[50..51]":"校验和",
}


def read_pages(dll, pages: int = 64) -> list[bytes]:
    """读取 N 个 64 字节内存页，返回列表"""
    wake = bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0, 0, 0, 0])
    dll.SetFeature(wake, len(wake))
    time.sleep(0.3)

    result = []
    for i in range(pages):
        # 注意：GetFeature 的 Report ID 不可为 0，用 0x04
        buf = ctypes.create_string_buffer(bytes([0x04]) + b"\x00" * 0x3F, 0x40)
        r = dll.GetFeature(buf, 0x40)
        raw = bytes(buf.raw)
        result.append(raw)
        # 检查是否全零（可能是休眠或无数据）
        if i < 3 and all(b == 0 for b in raw[1:]):
            print(f"  ⚠️  P{i:02d} 返回全零，设备可能休眠或断链")
    return result


def format_page(i: int, raw: bytes, annotate: bool = True) -> str:
    """格式化一页输出"""
    # 基础行
    hex_str = raw.hex(" ")
    line = f"P{i:02d} {hex_str}"

    # 添加标注
    if annotate:
        note = PAGE_ANNOTATIONS.get(i, "")
        if note:
            line += f"  # {note}"

        # 检查全零
        if all(b == 0 for b in raw):
            line += "  [全零]"
        elif all(b == 0 for b in raw[1:]):
            line += "  [仅 Report ID]"

    return line


def format_compare(i: int, a_raw: bytes, b_raw: bytes) -> str:
    """对比两页，标注差异"""
    hex_a = a_raw.hex(" ")
    hex_b = b_raw.hex(" ")

    if a_raw == b_raw:
        return f"P{i:02d} ✅ 相同  {hex_a}"

    # 找出差异字节
    diff_positions = []
    for j in range(min(len(a_raw), len(b_raw))):
        if a_raw[j] != b_raw[j]:
            diff_positions.append(j)

    diff_str = ",".join(str(d) for d in diff_positions[:20])
    if len(diff_positions) > 20:
        diff_str += f"...({len(diff_positions)}处差异)"

    note = PAGE_ANNOTATIONS.get(i, "")
    tag = f"  # {note}" if note else ""

    return (f"P{i:02d} ❌ 差异({diff_str}){tag}\n"
            f"     A: {hex_a}\n"
            f"     B: {hex_b}")


# ═══════════════════════════════════════════════════════════════
# 主入口
# ═══════════════════════════════════════════════════════════════

def cmd_read(label: str, pages: int):
    """读取快照"""
    os.makedirs(SNAPSHOT_DIR, exist_ok=True)

    print(f"打开设备 {VID:04X}:{PID:04X} ...")
    dll = ctypes.WinDLL(DLL_PATH)
    dll.Set_VIDPID.argtypes = [ctypes.c_uint, ctypes.c_uint]
    dll.Set_VIDPID.restype = ctypes.c_int
    dll.Open_FeatureDevice.restype = ctypes.c_int
    dll.SetFeature.argtypes = [ctypes.c_char_p, ctypes.c_uint]
    dll.SetFeature.restype = ctypes.c_int
    dll.GetFeature.argtypes = [ctypes.c_char_p, ctypes.c_uint]
    dll.GetFeature.restype = ctypes.c_int
    dll.Close_FeatureDevice.restype = ctypes.c_int

    dll.Set_VIDPID(VID, PID)
    if not dll.Open_FeatureDevice():
        print("❌ Open_FeatureDevice 失败（鼠标可能休眠/未连接）")
        sys.exit(1)

    print(f"读取 {pages} 页...")
    pages_data = read_pages(dll, pages)
    dll.Close_FeatureDevice()

    lines = [
        f"# M618XSD 内存快照: {label}",
        f"# 时间: {time.strftime('%Y-%m-%d %H:%M:%S')}",
        f"# 页数: {pages}",
        f"# 每页: 64 字节",
        f"# 注意: GetFeature 是顺序指针读取，Report ID 不影响内容",
        f"",
    ]
    for i, raw in enumerate(pages_data):
        lines.append(format_page(i, raw))

    out = os.path.join(SNAPSHOT_DIR, f"walk_{label}.txt")
    with open(out, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    print(f"✅ 已保存: {out}")


def cmd_compare(a_label: str, b_label: str):
    """对比两个快照"""
    a_path = os.path.join(SNAPSHOT_DIR, f"walk_{a_label}.txt")
    b_path = os.path.join(SNAPSHOT_DIR, f"walk_{b_label}.txt")

    if not os.path.exists(a_path):
        print(f"❌ 文件不存在: {a_path}")
        sys.exit(1)
    if not os.path.exists(b_path):
        print(f"❌ 文件不存在: {b_path}")
        sys.exit(1)

    # 解析快照文件
    def parse_snapshot(path):
        result = {}
        with open(path, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                # 格式: P00 <hex>
                parts = line.split()
                if len(parts) >= 2 and parts[0].startswith("P"):
                    idx = int(parts[0][1:])
                    try:
                        raw = bytes.fromhex("".join(parts[1:]))
                        result[idx] = raw
                    except ValueError:
                        pass
        return result

    a_data = parse_snapshot(a_path)
    b_data = parse_snapshot(b_path)

    all_pages = sorted(set(list(a_data.keys()) + list(b_data.keys())))

    print(f"对比: {a_label} vs {b_label}")
    print(f"  A 共 {len(a_data)} 页, B 共 {len(b_data)} 页")
    print()

    diff_count = 0
    for i in all_pages:
        a_raw = a_data.get(i)
        b_raw = b_data.get(i)
        if a_raw is None:
            print(f"P{i:02d} 仅 B 存在")
            diff_count += 1
        elif b_raw is None:
            print(f"P{i:02d} 仅 A 存在")
            diff_count += 1
        elif a_raw != b_raw:
            print(format_compare(i, a_raw, b_raw))
            print()
            diff_count += 1

    if diff_count == 0:
        print("✅ 完全一致，无差异")
    else:
        print(f"总计 {diff_count} 页存在差异")


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)

    if sys.argv[1] == "compare":
        if len(sys.argv) < 4:
            print("用法: python walk_pages.py compare <a_label> <b_label>")
            sys.exit(1)
        cmd_compare(sys.argv[2], sys.argv[3])
    else:
        label = sys.argv[1]
        pages = 64
        if "--pages" in sys.argv:
            idx = sys.argv.index("--pages")
            pages = int(sys.argv[idx + 1])
        cmd_read(label, pages)


if __name__ == '__main__':
    main()