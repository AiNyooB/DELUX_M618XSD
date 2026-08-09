# -*- coding: utf-8 -*-
"""
灯光恢复脚本 — 发送完整「应用全部」序列恢复设备状态
"""
import ctypes
import os
import time

DLL_PATH = os.path.join(
    r"C:\Users\fresh\Downloads\618XSD\extracted\app", "hiddriver_ms_4.dll"
)
VID, PID = 0x1D57, 0xFA60


def send_report(dll, data, label=""):
    r = dll.SetFeature(data, len(data))
    print(f"  {label}: {data.hex(' ')} -> {r}")
    time.sleep(0.3)
    return r


def main():
    dll = ctypes.WinDLL(DLL_PATH)
    dll.Set_VIDPID.argtypes = [ctypes.c_uint, ctypes.c_uint]
    dll.Set_VIDPID.restype = ctypes.c_int
    dll.Open_FeatureDevice.restype = ctypes.c_int
    dll.SetFeature.argtypes = [ctypes.c_char_p, ctypes.c_uint]
    dll.SetFeature.restype = ctypes.c_int
    dll.Close_FeatureDevice.restype = ctypes.c_int

    dll.Set_VIDPID(VID, PID)
    if not dll.Open_FeatureDevice():
        print("❌ Open_FeatureDevice 失败")
        return

    print("=== 发送完整「应用全部」序列 ===")

    # 1. 0x0C 唤醒
    send_report(dll, bytes([0x0C, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0, 0, 0, 0]), "唤醒")

    # 2. 0x04 DPI 配置（light_2 抓包默认值）
    dpi = bytes.fromhex(
        "04 38 01 00 00 1f 10 10 20 b0 40 60 a0 00 00 00 "
        "03 04 06 09 0f 00 00 00 02 ff 00 00 00 ff 00 00 00 "
        "ff ff 00 ff ff ff 00 ff 00 ff ff 40 00 ff ff ff 02 0f ab 00 00 00 00"
    )
    send_report(dll, dpi, "DPI")

    # 3. 0x05 灯光配置（循环呼吸, 速度默认, 移动关灯开启, 休眠1min）
    light = bytes([0x05, 0x0F, 0x01, 0x03, 0x03, 0xA8, 0x00, 0x00, 0xFF, 0x02, 0x03, 0x01, 0xB2, 0x00, 0x00])
    send_report(dll, light, "灯光")

    # 4. 0x06 回报率（250Hz）
    rate = bytes([0x06, 0x09, 0x01, 0x04, 0xFB, 0x00, 0x00, 0x00, 0x00])
    send_report(dll, rate, "回报率")

    # 5. 0x08 按键映射（light_2 抓包默认值）
    buttons = bytes.fromhex(
        "08 3b 01 02 00 00 03 00 00 06 00 00 05 00 00 04 00 00 "
        "0d 00 00 01 00 00 01 00 00 01 00 00 01 00 00 01 00 00 "
        "01 00 00 01 00 00 01 00 00 0b 00 00 0c 00 00 09 00 00 0a 00 00 00 53"
    )
    send_report(dll, buttons, "按键")

    dll.Close_FeatureDevice()
    print("=== 完成 ===")


if __name__ == "__main__":
    main()