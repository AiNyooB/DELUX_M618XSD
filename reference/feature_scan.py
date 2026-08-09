# -*- coding: utf-8 -*-
"""
feature_scan.py — 只读探测：枚举特性接口所有 Feature Report ID，找"当前档位"字段。
  - 纯 GetFeature 读取，不写任何配置，安全。
  - 对 Report ID 1..63 逐个 GetFeature(64字节)，过滤全0/报错。
  - 两轮扫描之间请你按一次 DPI 键（让档位变一下），脚本对比哪字节变了。

用法（必须 32 位 Python）：
  C:\tmp\re32\py\python.exe feature_scan.py
先确保官方 Mouse.exe 已完全退出。
"""
import ctypes
import time

DLL_PATH = r"C:\Users\fresh\Downloads\618XSD\extracted\app\hiddriver_ms_4.dll"
VID, PID = 0x1D57, 0xFA60
FEAT_LEN = 64
ID_LO, ID_HI = 1, 63


def scan_once(dll):
    """返回 {report_id: bytes(64)}，仅含非空且非全零的结果。"""
    out = {}
    for rid in range(ID_LO, ID_HI + 1):
        buf = ctypes.create_string_buffer(FEAT_LEN)
        buf[0] = rid & 0xFF
        try:
            r = dll.GetFeature(buf, FEAT_LEN)
        except Exception:
            continue
        if r != 1:
            continue
        raw = bytes(buf[0:FEAT_LEN])
        if raw == b"\x00" * FEAT_LEN:
            continue
        out[rid] = raw
    return out


def dump(round_name, data):
    print("\n=== %s：命中 %d 个非空 Report ID ===" % (round_name, len(data)))
    for rid in sorted(data):
        raw = data[rid]
        # 压缩显示：去掉尾部连续 0
        t = raw.rstrip(b"\x00")
        hexs = " ".join("%02x" % b for b in t)
        print("  [0x%02X] len=%d  %s" % (rid, len(t), hexs))


def main():
    try:
        dll = ctypes.WinDLL(DLL_PATH)
    except Exception as e:
        print("[!] 加载 DLL 失败: %r" % e)
        return
    dll.Set_VIDPID.argtypes = [ctypes.c_uint, ctypes.c_uint]
    dll.Set_VIDPID.restype = ctypes.c_int
    dll.Open_FeatureDevice.restype = ctypes.c_int
    dll.GetFeature.argtypes = [ctypes.c_char_p, ctypes.c_uint]
    dll.GetFeature.restype = ctypes.c_int
    dll.Close_FeatureDevice.restype = ctypes.c_int

    dll.Set_VIDPID(VID, PID)
    if not dll.Open_FeatureDevice():
        print("[!] Open_FeatureDevice 失败，请确认接收器已插、Mouse.exe 已退出")
        return
    print("[+] 特性设备已打开，开始只读扫描（不写任何配置）")

    try:
        r1 = scan_once(dll)
        dump("第1轮（按键前）", r1)

        input("\n>>> 现在请按【一次】DPI 键（让档位变一下），然后回车继续扫描第2轮 ...")

        # 等一下让设备稳定
        time.sleep(0.5)
        r2 = scan_once(dll)
        dump("第2轮（按键后）", r2)

        # 对比变化
        all_ids = sorted(set(r1) | set(r2))
        changed = []
        for rid in all_ids:
            a = r1.get(rid)
            b = r2.get(rid)
            if a != b:
                changed.append(rid)
        print("\n=== 差分结论 ===")
        if not changed:
            print("[X] 两轮之间没有任何 Feature Report 随 DPI 按键变化 —— 当前档位不在 GetFeature 通道里，上位机改用『写入+本地记忆』。")
        else:
            print("[!] 以下 Report ID 在按键后发生变化，逐个看哪个字节对应档位：")
            for rid in changed:
                a = r1.get(rid)
                b = r2.get(rid)
                la = a.rstrip(b"\x00") if a else b""
                lb = b.rstrip(b"\x00") if b else b""
                print("  [0x%02X] 前=%s" % (rid, " ".join("%02x" % x for x in la)))
                print("        后=%s" % (" ".join("%02x" % x for x in lb)))
    finally:
        dll.Close_FeatureDevice()
        print("\n[+] 已关闭特性设备，脚本结束。")


if __name__ == "__main__":
    main()
