#!/usr/bin/env python3
# 完整序列覆盖宏 ID 4（保留配置，只改按键 Z→B）
import ctypes, sys, os, time

DLL_PATH = r"C:\Users\fresh\Downloads\618XSD\extracted\app\hiddriver_ms_4.dll"
VID, PID = 0x1D57, 0xFA60

# 0x09 宏数据（ID 4，按键 B，任意键停止播放）
CHUNK0 = bytes.fromhex(
    "09 40 04 00 01 00 00 00 01 00 00 00 00 00 00 00"
    "00 00 00 00 00 00 00 00 00 00 00 00 00 02 01 05"
    "81 05 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
    "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
)
CHUNK1 = bytes.fromhex(
    "09 40 04 01 00 00 00 00 00 00 00 00 00 00 00 00"
    "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
    "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
    "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
)
CHUNK2 = bytes.fromhex(
    "09 0c 04 02 00 00 00 00 00 00 00 90 00 00 00 00"
    "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
    "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
    "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00"
)

def main():
    print("=" * 60)
    print("  完整序列覆盖宏 ID 4（Z→B）")
    print("=" * 60)

    if not os.path.exists(DLL_PATH):
        print(f"  DLL 不存在: {DLL_PATH}"); sys.exit(1)

    print("\n  ⚠️  确保 Mouse.exe 已关闭!")

    dll = ctypes.WinDLL(DLL_PATH)
    dll.Set_VIDPID(ctypes.c_ushort(VID), ctypes.c_ushort(PID))
    dll.Open_FeatureDevice()

    # macro_4 的完整序列
    wakeup = bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0x00, 0x00, 0x00, 0x00])
    r04 = bytes.fromhex("04 38 01 00 00 1f 10 10 20 b0 40 60 a0 00 00 00 03 04 06 09 0f 00 00 00 02 ff 00 00 00 ff 00 00 00 ff ff 00 ff ff ff 00 ff 00 ff ff 40 00 ff ff ff 02 0f ab 00 00 00 00")
    r05 = bytes.fromhex("05 0f 01 04 03 a8 00 00 ff 01 03 01 b2 00 00")
    r06 = bytes.fromhex("06 09 01 02 fd 00 00 00 00")
    # 官方 0x08 映射，entry[3]=12 00 04（宏 ID 4 不变）
    r08 = bytes.fromhex("08 3b 01 02 00 00 03 00 00 06 00 00 12 00 04 04 00 00 0d 00 00 01 00 00 01 00 00 01 00 00 01 00 00 01 00 00 01 00 00 01 00 00 01 00 00 0b 00 00 0c 00 00 09 00 00 0a 00 00 00 64")

    steps = [
        (wakeup, 0.0),
        (r04, 1.0), (r04, 0.5),
        (r05, 1.0),
        (r06, 1.0), (r06, 0.5),  # 0x06 发两次（和保存配置时一致）
        (r08, 1.0),
        (CHUNK0, 1.0), (CHUNK1, 1.0), (CHUNK2, 1.0),
    ]

    for data, delay in steps:
        r = dll.SetFeature(ctypes.c_char_p(bytes(data)), ctypes.c_int(len(data)))
        print(f"  ({len(data)}B) 返回={r}")
        if delay > 0: time.sleep(delay)

    dll.Close_FeatureDevice()
    print(f"\n  ✅ 完成! 后退键宏 Z→B（保留播放配置）")
    print(f"  💡 按后退键测试")

if __name__ == '__main__':
    main()