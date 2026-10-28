# Disposable SQLite/Access data checks plus off-screen WPF preview checks. Run in x86 Windows PowerShell -STA.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
[void][Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'TXDB\bin\Debug\TXDB.exe'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'TXDB\bin\Debug\TX.WpfUI.dll'))
$probeRoot = Join-Path ([IO.Path]::GetTempPath()) ('TXDB-Preview-Probe-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $probeRoot)
$checks = 0
$flags = [Reflection.BindingFlags]'Instance,NonPublic'

function Assert-Equal($actual, $expected, [string]$message) {
    if ($actual -cne $expected) { throw ($message + ': expected=' + $expected + ', actual=' + $actual) }
    $script:checks++
}
function Run-Sql([string]$sql) {
    $connection = $provider.CreateConnection($profile)
    try { $connection.Open(); $command = $connection.CreateCommand(); $command.CommandText = $sql; [void]$command.ExecuteNonQuery() }
    finally { if ($null -ne $command) { $command.Dispose() }; $connection.Dispose() }
}
function Invoke-View([string]$method, [object[]]$arguments = @()) {
    $invokeArguments = New-Object object[] $arguments.Length
    for ($index=0; $index -lt $arguments.Length; $index++) {
        if ($null -ne $arguments[$index]) { $invokeArguments[$index] = $arguments[$index].PSObject.BaseObject }
    }
    return $view.GetType().GetMethod($method, $flags).Invoke($view, $invokeArguments)
}
function Render-Layout {
    $view.Measure((New-Object Windows.Size(1200,650)))
    $view.Arrange((New-Object Windows.Rect(0,0,1200,650)))
    $view.UpdateLayout()
    $view.Dispatcher.Invoke([Action]{}, [Windows.Threading.DispatcherPriority]::ApplicationIdle)
}
function Find-Visual($root, [type]$type) {
    if ($type.IsInstanceOfType($root)) { return $root }
    for ($index=0; $index -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($root); $index++) {
        $found = Find-Visual ([Windows.Media.VisualTreeHelper]::GetChild($root, $index)) $type
        if ($null -ne $found) { return $found }
    }
    return $null
}

try {
    $app = New-Object TXDB.App
    $app.InitializeComponent()
    [TX.WpfUI.Theming.TXThemeManager]::ApplyTheme([TX.WpfUI.Theming.TXThemeManager]::Themes[0])
    $view = New-Object TXDB.Views.TablePreviewTabView
    $grid = $view.FindName('ResultGrid')
    $add = $view.FindName('AddRowButton')
    $delete = $view.FindName('DeleteRowButton')
    $commit = $view.FindName('CommitChangesButton')
    $cancel = $view.FindName('CancelChangesButton')

    foreach ($kind in @('SQLite', 'Access')) {
        $profile = New-Object TXDB.Models.ConnectionProfile
        $profile.ProviderType = [TXDB.Models.DatabaseProviderType]::$kind
        if ($kind -eq 'SQLite') {
            $profile.FilePath = Join-Path $probeRoot 'preview.sqlite'
            $provider = New-Object TXDB.Data.SQLiteDatabaseProvider
        } else {
            $profile.FilePath = Join-Path $probeRoot 'preview.mdb'
            $profile.RawConnectionString = 'Provider=Microsoft.Jet.OLEDB.4.0;Data Source=' + $profile.FilePath
            $catalog = New-Object -ComObject ADOX.Catalog
            [void]$catalog.Create($profile.RawConnectionString)
            $catalog.ActiveConnection.Close()
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($catalog)
            $provider = New-Object TXDB.Data.AccessDatabaseProvider
        }
        Run-Sql 'CREATE TABLE PreviewProbe (Id INTEGER PRIMARY KEY, Label VARCHAR(80), Score INTEGER)'
        $node = New-Object TXDB.Models.DatabaseObjectNode
        $node.Name = 'PreviewProbe'; $node.Kind = [TXDB.Models.DatabaseObjectKind]::Table
        if ($kind -eq 'SQLite') { $node.DatabaseName = 'main'; $node.SchemaName = 'main' }
        $result = $provider.PreviewTable($profile, $node, 1, 100)
        Assert-Equal $result.Table.Rows.Count 0 "$kind empty table"
        Assert-Equal ($result.Table.Columns.ColumnName -join ',') 'Id,Label,Score' "$kind empty preview retains ordered columns"
        $query = $provider.Execute($profile, 'SELECT Id, Label FROM PreviewProbe WHERE 1=0')
        Assert-Equal $query.Table.Columns.Count 2 "$kind empty query retains columns"
        $view.Initialize($profile, $node)
        $view.SetResult($result)
        Render-Layout
        Assert-Equal $grid.Columns.Count 3 "$kind empty WPF grid has column headers"
        Assert-Equal $add.IsEnabled $true "$kind empty table permits adding"
        Assert-Equal $delete.IsEnabled $false "$kind no selection disables delete"
        Assert-Equal $commit.IsEnabled $false "$kind clean table disables submit"
        Assert-Equal $cancel.IsEnabled $false "$kind clean table disables cancel"
        [void](Invoke-View 'AddRowButton_Click' @($null, $null))
        Assert-Equal $grid.Items.Count 1 "$kind add row into empty table"
        $result.Table.Rows[0]['Id'] = 1
        $result.Table.Rows[0]['Label'] = 'first'
        Assert-Equal $commit.IsEnabled $true "$kind insert enables submit immediately"
        Assert-Equal ($provider.ApplyTableChanges($profile, $node, $result.Table.GetChanges())) 1 "$kind empty-table insert persists"
        Run-Sql "INSERT INTO PreviewProbe (Id,Label) VALUES (2,'second')"
        Run-Sql "INSERT INTO PreviewProbe (Id,Label) VALUES (3,'third')"
        $result = $provider.PreviewTable($profile, $node, 1, 100)
        $view.SetResult($result)
        Render-Layout
        Assert-Equal $grid.HeadersVisibility ([Windows.Controls.DataGridHeadersVisibility]::All) "$kind row selector enabled"
        $firstRow = $grid.ItemContainerGenerator.ContainerFromIndex(0)
        Assert-Equal ($null -ne $firstRow) $true "$kind visible first row realized"
        $grid.CurrentCell = New-Object Windows.Controls.DataGridCellInfo($grid.Items[0], $grid.Columns[1])
        Assert-Equal $grid.BeginEdit() $true "$kind enter cell editor"
        Render-Layout
        $editor = Find-Visual $firstRow ([Windows.Controls.TextBox])
        Assert-Equal ($null -ne $editor) $true "$kind cell editor present"
        $editor.Text = 'changed before leaving cell'
        Assert-Equal $commit.IsEnabled $true "$kind typing enables submit before changing row"
        [void](Invoke-View 'CancelChangesButton_Click' @($null, $null))
        Assert-Equal $commit.IsEnabled $false "$kind cancel restores clean submit state"
        $grid.UnselectAll(); $grid.UnselectAllCells()
        $header = Find-Visual $firstRow ([Windows.Controls.Primitives.DataGridRowHeader])
        Assert-Equal ($null -ne $header) $true "$kind native row header exists"
        Assert-Equal $header.ActualWidth 24 "$kind narrow row header layout"
        # Invoke the native row-header click, not a synthetic data-column selection.
        [void]$header.GetType().GetMethod('OnClick', $flags).Invoke($header, @())
        Assert-Equal $grid.SelectedItems.Count 1 "$kind row header selects whole row"
        Assert-Equal $grid.SelectedCells.Count 3 "$kind row header includes every cell"
        $grid.SelectedItems.Add($grid.Items[2])
        Assert-Equal @(Invoke-View 'GetSelectedRows').Count 2 "$kind two row headers select two distinct records"
        Assert-Equal $delete.IsEnabled $true "$kind selected rows enable delete"
        $grid.UnselectAll()
        $grid.UnselectAllCells()
        Assert-Equal $delete.IsEnabled $false "$kind clearing selection disables delete"
        $grid.SelectedCells.Add((New-Object Windows.Controls.DataGridCellInfo($grid.Items[0], $grid.Columns[0])))
        $grid.SelectedCells.Add((New-Object Windows.Controls.DataGridCellInfo($grid.Items[0], $grid.Columns[1])))
        $grid.SelectedCells.Add((New-Object Windows.Controls.DataGridCellInfo($grid.Items[2], $grid.Columns[0])))
        $selected = @(Invoke-View 'GetSelectedRows')
        Assert-Equal $selected.Count 2 "$kind cell range selects and deduplicates two records"
        Assert-Equal $delete.IsEnabled $true "$kind selected cells enable delete"
        $selectedRowsList = New-Object 'System.Collections.Generic.List[System.Data.DataRowView]'
        foreach ($row in $selected) { $selectedRowsList.Add($row) }
        $script:submissionCount = 0
        $script:submittedChanges = $null
        $onCommit = { param($sender, $eventArgs)
            $script:submissionCount++
            $script:submittedChanges = $eventArgs.Changes
        }
        $view.add_CommitRequested($onCommit)
        try { [void](Invoke-View 'DeleteRowsAndSubmit' @(,$selectedRowsList)) }
        finally { $view.remove_CommitRequested($onCommit) }
        Assert-Equal $script:submissionCount 1 "$kind confirmed deletion immediately requests one submit"
        Assert-Equal $script:submittedChanges.Rows.Count 2 "$kind submit contains both distinct rows"
        Assert-Equal $view.IsDataOperationInProgress $true "$kind batch submit blocks duplicate operations"
        Assert-Equal ($provider.ApplyTableChanges($profile, $node, $script:submittedChanges)) 2 "$kind batch deletion persists in one transaction"
        $remaining = $provider.PreviewTable($profile, $node, 1, 100)
        Assert-Equal $remaining.Table.Rows.Count 1 "$kind batch deletion preserves unselected record"
        Assert-Equal ([int]$remaining.Table.Rows[0]['Id']) 2 "$kind correct record remains"
        $view.SetResult($remaining)
        $view.FindName('ResultGrid').SelectedItem = $grid.Items[0]
        Render-Layout
        # A page beyond the last row must still provide the schema.
        Assert-Equal ($provider.PreviewTable($profile, $node, 2, 100).Table.Columns.Count) 3 "$kind empty later page retains schema"
        if ($kind -eq 'SQLite') {
            Assert-Equal ($provider.Execute($profile, 'SELECT Id AS Same, Label AS Same, Score FROM PreviewProbe').Table.Columns.Count) 3 'Duplicate query labels retain all ordinals'
        }
        $column = New-Object TXDB.Models.DatabaseObjectNode
        $column.Name = 'Label'; $column.Kind = [TXDB.Models.DatabaseObjectKind]::Column
        $node.Children.Add($column)
        $filters = New-Object 'System.Collections.Generic.List[TXDB.Models.TableFilterCondition]'
        $filter = New-Object TXDB.Models.TableFilterCondition
        $filter.IsEnabled = $true; $filter.ColumnName = 'Label'; $filter.OperatorCode = 'Equal'; $filter.Value = 'missing'
        $filters.Add($filter)
        $filtered = $provider.PreviewTable($profile, $node, 1, 100, $filters)
        Assert-Equal $filtered.Table.Rows.Count 0 "$kind filter returns zero rows"
        Assert-Equal $filtered.Table.Columns.Count 3 "$kind zero filtered rows retain schema"
        Run-Sql 'CREATE TABLE NoKeyProbe (Id INTEGER, Label VARCHAR(80))'
        Run-Sql "INSERT INTO NoKeyProbe VALUES (1,'unique')"
        Run-Sql "INSERT INTO NoKeyProbe VALUES (2,'duplicate')"
        Run-Sql "INSERT INTO NoKeyProbe VALUES (2,'duplicate')"
        $noKeyNode = New-Object TXDB.Models.DatabaseObjectNode
        $noKeyNode.Name = 'NoKeyProbe'; $noKeyNode.Kind = [TXDB.Models.DatabaseObjectKind]::Table
        $noKeyNode.DatabaseName = $node.DatabaseName; $noKeyNode.SchemaName = $node.SchemaName
        $noKeyTable = $provider.PreviewTable($profile, $noKeyNode, 1, 100).Table
        $noKeyTable.AcceptChanges()
        $noKeyTable.Rows[0].Delete(); $noKeyTable.Rows[1].Delete()
        $rejected = $false
        try { [void]$provider.ApplyTableChanges($profile, $noKeyNode, $noKeyTable.GetChanges()) } catch { $rejected = $true }
        Assert-Equal $rejected $true "$kind ambiguous no-key deletion rejected"
        Assert-Equal ($provider.PreviewTable($profile, $noKeyNode, 1, 100).Table.Rows.Count) 3 "$kind failed batch deletion rolls back every row"
        Write-Output ($kind + ' preview checks passed')
    }

    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap(1200,650,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($view)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $previewPath = Join-Path ([IO.Path]::GetTempPath()) ('txdb-preview-' + [Guid]::NewGuid().ToString('N') + '.png')
    $stream = [IO.File]::Create($previewPath)
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
    Write-Output ('PASS: ' + $checks + ' preview assertions')
    Write-Output $previewPath
}
finally {
    [Data.OleDb.OleDbConnection]::ReleaseObjectPool()
    [GC]::Collect(); [GC]::WaitForPendingFinalizers()
    foreach ($file in Get-ChildItem -LiteralPath $probeRoot -File) { Remove-Item -LiteralPath $file.FullName -Force }
    Remove-Item -LiteralPath $probeRoot -Force
}
