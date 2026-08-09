import io

p = r'C:\Users\fresh\Downloads\618XSD\MouseDriverClient\MainViewModel.cs'
with io.open(p, 'r', encoding='utf-8-sig') as f:
    text = f.read()

# 1) 插入电池属性（放在 Connect region 之后）
prop_anchor = '            _hid.Wake();\n        }\n        #endregion'
if prop_anchor not in text:
    raise SystemExit('ERROR: prop anchor not found')

prop_new = prop_anchor + '''
        #endregion

        #region 电池
        private int? _batteryPercent;
        public int? BatteryPercent
        {
            get => _batteryPercent;
            set => Set(ref _batteryPercent, value);
        }

        private string _batteryChargeText = "\u2014";
        public string BatteryChargeText
        {
            get => _batteryChargeText;
            set => Set(ref _batteryChargeText, value);
        }
        #endregion'''

text = text.replace(prop_anchor, prop_new, 1)

# 2) 订阅 BatteryChanged（放在 StartInputListener 之后）
sub_anchor = '                _hid.StartInputListener();'
if sub_anchor not in text:
    raise SystemExit('ERROR: sub anchor not found')

sub_new = sub_anchor + '''
                _hid.BatteryChanged += (cs, pct) =>
                {
                    var disp = _uiDispatcher ?? Application.Current?.Dispatcher;
                    var txt = cs switch
                    {
                        1 => "\u672a\u5145\u7535/\u6ee1\u7535",
                        2 => "\u5145\u7535\u4e2d",
                        3 => "\u5145\u7535\u5b8c\u6210",
                        4 => "\u63d2\u5165\u68c0\u6d4b",
                        _ => "\u672a\u77e5(" + cs + ")"
                    };
                    if (disp != null)
                        disp.Invoke(() => { BatteryPercent = pct; BatteryChargeText = txt; });
                    else
                        { BatteryPercent = pct; BatteryChargeText = txt; }
                    Log($"[电池] {pct}% / {txt}");
                };'''

text = text.replace(sub_anchor, sub_new, 1)

with io.open(p, 'w', encoding='utf-8-sig') as f:
    f.write(text)

print('OK: BatteryPercent/BatteryChargeText added + BatteryChanged subscribed')
