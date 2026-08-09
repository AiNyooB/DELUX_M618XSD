$ErrorActionPreference = 'Stop'
$p = 'C:\Users\fresh\Downloads\618XSD\MouseDriverClient\MainWindow.xaml'
$enc = [System.Text.Encoding]::UTF8
$text = [System.IO.File]::ReadAllText($p, $enc)

$old = '            <ListBox x:Name="LstLog" ItemsSource="{Binding LogLines}" ScrollViewer.VerticalScrollBarVisibility="Auto" Background="#FFFFFF" Foreground="#000000" BorderThickness="1" BorderBrush="#7F9DB9" FontFamily="Consolas" FontSize="11"/>'
$new = '            <TextBox x:Name="TxtLog" IsReadOnly="True" TextWrapping="Wrap" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto" AcceptsReturn="True" IsUndoEnabled="False" Background="#FFFFFF" Foreground="#000000" BorderThickness="1" BorderBrush="#7F9DB9" FontFamily="Consolas" FontSize="11" Text="{Binding LogText, Mode=OneWay}"/>'

if ($text.Contains($old) -eq $false) {
    Write-Host "ERROR: anchor not found"
    exit 1
}
$text = $text.Replace($old, $new)
[System.IO.File]::WriteAllText($p, $text, $enc)
Write-Host "OK: ListBox -> TextBox bound to LogText (copyable)"
