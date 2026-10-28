# Off-screen WPF binding/state/render check. No configured connection is opened.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
[void][Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'TXDB\bin\Debug\TXDB.exe'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'TXDB\bin\Debug\TX.WpfUI.dll'))
$app = New-Object TXDB.App
$app.InitializeComponent()
[TX.WpfUI.Theming.TXThemeManager]::ApplyTheme([TX.WpfUI.Theming.TXThemeManager]::Themes[0])
$view = New-Object TXDB.Views.TableStructureTabView
$profile = New-Object TXDB.Models.ConnectionProfile
$profile.ProviderType = [TXDB.Models.DatabaseProviderType]::Access
$node = New-Object TXDB.Models.DatabaseObjectNode
$node.Name = ''; $node.DatabaseName = 'sample.mdb'; $node.Kind = [TXDB.Models.DatabaseObjectKind]::Table
[void]$view.InitializeAsync($profile, $node, $true).GetAwaiter().GetResult()
if ($view.FindName('SaveButton').IsEnabled -or $view.FindName('CancelButton').IsEnabled) { throw 'Untouched new-table draft must disable save/cancel' }
$view.FindName('NameBox').Text='Products'
if (!$view.FindName('SaveButton').IsEnabled) { throw 'Entering table name enables creation' }
$snapshot = New-Object TXDB.Data.TableSchemaSnapshot
foreach ($column in $view.Columns) { $column.OriginalName = $column.Name; $snapshot.Columns.Add($column.Clone()) }
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
$view.GetType().GetField('original', $flags).SetValue($view, $snapshot)
$view.GetType().GetProperty('IsNew').SetValue($view, $false, $null)
$view.GetType().GetMethod('UpdateButtons', $flags).Invoke($view, @())
$save = $view.FindName('SaveButton'); $cancel = $view.FindName('CancelButton')
if ($save.IsEnabled -or $cancel.IsEnabled) { throw 'Unchanged structure must disable save/cancel' }
$view.Columns[0].Name = 'ProductId'
if (!$save.IsEnabled -or !$cancel.IsEnabled) { throw 'Typing into the current cell must enable save/cancel immediately' }
$view.Columns[0].Name = 'Id'
if ($save.IsEnabled -or $cancel.IsEnabled) { throw 'Reverting the value must clear dirty state' }
$view.Columns[0].AllowNull = $true
if ($view.Columns[0].AllowNull) { throw 'Primary key cannot allow NULL' }
$view.Columns[0].IsPrimaryKey = $false
if (!$save.IsEnabled) { throw 'Checkbox changes must enable save immediately' }
$view.Columns[0].IsPrimaryKey = $true
$textColumn = New-Object TXDB.Models.TableColumnDefinition
$textColumn.Name='ProductName'; $textColumn.TypeName=$view.TypeOptions[0]; $textColumn.TypeArguments='120'
$view.Columns.Add($textColumn)
$view.Measure((New-Object Windows.Size(1360,700)))
$view.Arrange((New-Object Windows.Rect(0,0,1360,700)))
$view.UpdateLayout()
$view.Dispatcher.Invoke([Action]{}, [Windows.Threading.DispatcherPriority]::ApplicationIdle)
$bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap(1360,700,96,96,[Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($view)
$encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$previewPath=Join-Path ([IO.Path]::GetTempPath()) ('txdb-table-designer-'+[Guid]::NewGuid().ToString('N')+'.png')
$stream=[IO.File]::Create($previewPath)
try { $encoder.Save($stream) } finally { $stream.Dispose() }
$view.Cleanup()
Write-Output 'PASS: WPF live dirty state, checkbox invariants, initial disabled buttons and off-screen render'
Write-Output $previewPath
