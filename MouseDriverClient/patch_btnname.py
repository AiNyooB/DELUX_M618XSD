import io

p = r'C:\Users\fresh\Downloads\618XSD\MouseDriverClient\MainViewModel.cs'
with io.open(p, 'r', encoding='utf-8-sig') as f:
    text = f.read()

old = '''        public List<OptionItem<int>> ButtonOptions { get; } = Enumerable.Range(0, 18)
            .Select(i => new OptionItem<int>($"键 {i + 1}", i)).ToList();'''

new = '''        private static readonly string[] _buttonNames = new[]
        {
            "\u5de6\u952e", "\u53f3\u952e", "\u524d\u8fdb", "\u540e\u9000", "\u4e2d\u952e", "DPI\u5faa\u73af",
            "\u672a\u4f7f\u75281", "\u672a\u4f7f\u75282", "\u672a\u4f7f\u75283", "\u672a\u4f7f\u75284",
            "\u672a\u4f7f\u75285", "\u672a\u4f7f\u75286", "\u672a\u4f7f\u75287", "\u672a\u4f7f\u75288",
            "\u5de6\u6eda", "\u53f3\u6eda", "\u4e0a\u6eda", "\u4e0b\u6eda"
        };
        public List<OptionItem<int>> ButtonOptions { get; } = Enumerable.Range(0, 18)
            .Select(i => new OptionItem<int>(_buttonNames[i], i)).ToList();'''

if old not in text:
    raise SystemExit('ERROR: old ButtonOptions not found')

text = text.replace(old, new, 1)

with io.open(p, 'w', encoding='utf-8-sig') as f:
    f.write(text)

print('OK: ButtonOptions now shows physical key names')
