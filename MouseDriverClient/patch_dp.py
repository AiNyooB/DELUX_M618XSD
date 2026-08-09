import io

files = [
    r'C:\Users\fresh\Downloads\618XSD\MouseDriverClient\MainWindow.xaml',
    r'C:\Users\fresh\Downloads\618XSD\MouseDriverClient\MainWindow.xaml.cs',
]

for p in files:
    with io.open(p, 'r', encoding='utf-8-sig') as f:
        text = f.read()
    n = 0
    # XAML 形式：DisplayMemberPath="Label"
    n += text.count('DisplayMemberPath="Label"')
    text = text.replace('DisplayMemberPath="Label"', 'DisplayMemberPath="Text"')
    # CS 形式：DisplayMemberPath = "Label"
    n += text.count('DisplayMemberPath = "Label"')
    text = text.replace('DisplayMemberPath = "Label"', 'DisplayMemberPath = "Text"')
    with io.open(p, 'w', encoding='utf-8-sig') as f:
        f.write(text)
    print(f'{p}: replaced {n} occurrence(s)')
