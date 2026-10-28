# Run after building with Windows PowerShell (x86), -NoProfile -STA.
# Uses only disposable databases; does not open configured user connections.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'TXDB\bin\Debug\TXDB.exe'))
$probeRoot = Join-Path ([IO.Path]::GetTempPath()) ('TXDB-Schema-Probe-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $probeRoot)
$checks = 0

function Assert-Equal($actual, $expected, [string]$message) {
    if ($actual -cne $expected) { throw ($message + ': expected=' + $expected + ', actual=' + $actual) }
    $script:checks++
}
function Read-Value([string]$sql) {
    $connection = $provider.CreateConnection($profile)
    try { $connection.Open(); $command = $connection.CreateCommand(); $command.CommandText = $sql; return $command.ExecuteScalar() }
    finally { if ($null -ne $command) { $command.Dispose() }; $connection.Dispose(); [Data.OleDb.OleDbConnection]::ReleaseObjectPool() }
}
function Run-Sql([string]$sql) {
    $connection = $provider.CreateConnection($profile)
    try { $connection.Open(); $command = $connection.CreateCommand(); $command.CommandText = $sql; [void]$command.ExecuteNonQuery() }
    finally { if ($null -ne $command) { $command.Dispose() }; $connection.Dispose(); [Data.OleDb.OleDbConnection]::ReleaseObjectPool() }
}
function New-Column([string]$name, [string]$type, [string]$typeArguments = '') {
    $column = New-Object TXDB.Models.TableColumnDefinition
    $column.Name = $name; $column.TypeName = $type; $column.TypeArguments = $typeArguments
    return $column
}
function Copy-Columns($snapshot) {
    $list = New-Object 'System.Collections.Generic.List[TXDB.Models.TableColumnDefinition]'
    foreach ($column in $snapshot.Columns) { $list.Add($column.Clone()) }
    return ,$list
}
function Save-Design($before, $columns) {
    $editor.Save($profile, $node, $before, $columns)
    [Data.OleDb.OleDbConnection]::ReleaseObjectPool()
}

try {
    $databasePath = Join-Path $probeRoot 'schema.mdb'
    $connectionString = 'Provider=Microsoft.Jet.OLEDB.4.0;Data Source=' + $databasePath
    $catalog = New-Object -ComObject ADOX.Catalog
    [void]$catalog.Create($connectionString)
    $catalog.ActiveConnection.Close()
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($catalog)
    $profile = New-Object TXDB.Models.ConnectionProfile
    $profile.RawConnectionString = $connectionString
    $profile.FilePath = $databasePath
    $profile.ProviderType = [TXDB.Models.DatabaseProviderType]::Access
    $provider = New-Object TXDB.Data.AccessDatabaseProvider
    $editor = New-Object TXDB.Data.TableSchemaEditor($provider)
    $types = $editor.GetTypeNames()
    $node = New-Object TXDB.Models.DatabaseObjectNode
    $node.Name = 'DesignProbe'; $node.Kind = [TXDB.Models.DatabaseObjectKind]::Table
    $columns = New-Object 'System.Collections.Generic.List[TXDB.Models.TableColumnDefinition]'
    $id = New-Column 'Id' $types[4]
    $id.IsPrimaryKey = $true; $id.IsIdentity = $true; $columns.Add($id)
    $name = New-Column 'Name' $types[0] '50'
    $name.AllowNull = $false; $name.DefaultSql = "'ready'"; $columns.Add($name)
    $score = New-Column 'Score' $types[8] '10,2'; $columns.Add($score)
    $flag = New-Column 'Flag' $types[10]; $flag.DefaultSql = 'False'; $columns.Add($flag)
    $memo = New-Column 'Memo' $types[1]; $columns.Add($memo)
    Save-Design $null $columns
    $before = $editor.Load($profile, $node)
    Assert-Equal $before.Columns.Count 5 'Access create table'
    Assert-Equal @($before.Columns | Where-Object IsIdentity).Count 1 'Access identity metadata'
    Assert-Equal @($before.Columns | Where-Object IsPrimaryKey).Count 1 'Access primary key metadata'
    Assert-Equal ($before.Columns | Where-Object Name -eq 'Name').DefaultSql "'ready'" 'Access default metadata'
    Run-Sql 'INSERT INTO DesignProbe (Score) VALUES (NULL)'
    Assert-Equal (Read-Value 'SELECT Name FROM DesignProbe') 'ready' 'Access default applied'
    $columns = Copy-Columns $before
    $name = $columns | Where-Object Name -eq 'Name'
    $name.Name = 'Label'; $name.TypeArguments = '80'; $name.AllowNull = $true
    $name.DefaultSql = "'changed'"; $name.Comment = 'Label description'
    [void]$columns.Remove(($columns | Where-Object Name -eq 'Memo'))
    $extra = New-Column 'Extra' $types[0] '20'; $extra.DefaultSql = "'extra'"; $columns.Add($extra)
    Save-Design $before $columns
    $before = $editor.Load($profile, $node)
    Assert-Equal $before.Columns.Count 5 'Access add/drop fields'
    Assert-Equal (Read-Value 'SELECT Label FROM DesignProbe WHERE Id=1') 'ready' 'Access rename preserves data'
    Assert-Equal ($before.Columns | Where-Object Name -eq 'Label').TypeArguments '80' 'Access type length changed'
    Assert-Equal ($before.Columns | Where-Object Name -eq 'Label').AllowNull $true 'Access nullable changed'
    Assert-Equal ($before.Columns | Where-Object Name -eq 'Label').Comment 'Label description' 'Access comment saved'
    Run-Sql 'INSERT INTO DesignProbe (Score) VALUES (2.5)'
    Assert-Equal (Read-Value 'SELECT Label FROM DesignProbe WHERE Id=2') 'changed' 'Access default changed'
    Assert-Equal (Read-Value 'SELECT Extra FROM DesignProbe WHERE Id=2') 'extra' 'Access new field default'

    Run-Sql 'INSERT INTO DesignProbe (Score) VALUES (3)'
    $columns = Copy-Columns $before
    ($columns | Where-Object Name -eq 'Extra').Name = 'Extra2'
    ($columns | Where-Object Name -eq 'Id').IsPrimaryKey = $false
    ($columns | Where-Object Name -eq 'Label').IsPrimaryKey = $true
    $rejected = $false
    try { Save-Design $before $columns } catch { $rejected = $true }
    Assert-Equal $rejected $true 'Access duplicate primary key rejected after rename'
    Assert-Equal (Read-Value 'SELECT Extra FROM DesignProbe WHERE Id=2') 'extra' 'Access rename compensated after DDL failure'
    Assert-Equal ($editor.Load($profile, $node).Columns | Where-Object Name -eq 'Label').AllowNull $true 'Access Required restored after DDL failure'
    Run-Sql 'DELETE FROM DesignProbe WHERE Id=3'
    $before = $editor.Load($profile, $node)
    $columns = Copy-Columns $before
    ($columns | Where-Object Name -eq 'Label').Name = 'Label2'
    ($columns | Where-Object Name -eq 'Score').AllowNull = $false
    $rejected = $false
    try { Save-Design $before $columns } catch { $rejected = $true }
    Assert-Equal $rejected $true 'Access invalid NOT NULL rejected'
    Assert-Equal (Read-Value 'SELECT Label FROM DesignProbe WHERE Id=1') 'ready' 'Access rename rolled back with later failure'

    $before = $editor.Load($profile, $node)
    $columns = Copy-Columns $before
    ($columns | Where-Object Name -eq 'Id').IsPrimaryKey = $false
    ($columns | Where-Object Name -eq 'Label').IsPrimaryKey = $true
    Save-Design $before $columns
    $before = $editor.Load($profile, $node)
    Assert-Equal ($before.Columns | Where-Object Name -eq 'Label').IsPrimaryKey $true 'Access primary key changed'
    $columns = Copy-Columns $before
    ($columns | Where-Object Name -eq 'Label').DefaultSql = ''
    Save-Design $before $columns
    Assert-Equal ($editor.Load($profile, $node).Columns | Where-Object Name -eq 'Label').DefaultSql '' 'Access default removed'
    $editor.DropTable($profile, $node)
    Write-Output ('Access checks complete: ' + $checks)

    $profile = New-Object TXDB.Models.ConnectionProfile
    $profile.ProviderType = [TXDB.Models.DatabaseProviderType]::SQLite
    $profile.FilePath = Join-Path $probeRoot 'schema.sqlite'
    $provider = New-Object TXDB.Data.SQLiteDatabaseProvider
    $editor = New-Object TXDB.Data.TableSchemaEditor($provider)
    $node.DatabaseName = 'main'; $node.SchemaName = 'main'
    $columns = New-Object 'System.Collections.Generic.List[TXDB.Models.TableColumnDefinition]'
    $id = New-Column 'Id' 'INTEGER'; $id.IsPrimaryKey = $true; $id.IsIdentity = $true; $columns.Add($id)
    $name = New-Column 'Name' 'TEXT'; $columns.Add($name)
    Save-Design $null $columns
    Run-Sql "INSERT INTO DesignProbe(Name) VALUES ('retained')"
    Run-Sql 'CREATE INDEX IX_Name ON DesignProbe(Name)'
    Run-Sql 'CREATE TABLE Log (Value TEXT)'
    Run-Sql 'CREATE TRIGGER TR_Name AFTER UPDATE ON DesignProbe BEGIN INSERT INTO Log VALUES (NEW.Name); END'
    $before = $editor.Load($profile, $node)
    $columns = Copy-Columns $before
    ($columns | Where-Object Name -eq 'Name').TypeName = 'VARCHAR'
    ($columns | Where-Object Name -eq 'Name').TypeArguments = '80'
    ($columns | Where-Object Name -eq 'Name').AllowNull = $false
    $extra = New-Column 'Extra' 'TEXT'; $extra.DefaultSql = "'a,b c'"; $columns.Add($extra)
    Save-Design $before $columns
    Assert-Equal (Read-Value 'SELECT Name FROM DesignProbe WHERE Id=1') 'retained' 'SQLite rebuild retains data'
    Assert-Equal (Read-Value 'SELECT Extra FROM DesignProbe WHERE Id=1') 'a,b c' 'SQLite expression quoted space/comma retained'
    Assert-Equal (Read-Value "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='IX_Name'") 1 'SQLite index preserved'
    Run-Sql "UPDATE DesignProbe SET Name='updated' WHERE Id=1"
    Assert-Equal (Read-Value 'SELECT Value FROM Log') 'updated' 'SQLite trigger preserved'
    $before = $editor.Load($profile, $node)
    $columns = Copy-Columns $before
    ($columns | Where-Object Name -eq 'Extra').Name = 'Renamed'
    Save-Design $before $columns
    Assert-Equal (Read-Value 'SELECT Renamed FROM DesignProbe WHERE Id=1') 'a,b c' 'SQLite simple rename retains data'
    $before = $editor.Load($profile, $node)
    $columns = Copy-Columns $before
    [void]$columns.Remove(($columns | Where-Object Name -eq 'Renamed'))
    Save-Design $before $columns
    Assert-Equal ($editor.Load($profile, $node).Columns.Count) 2 'SQLite field deletion'
    $before = $editor.Load($profile, $node)
    $columns = Copy-Columns $before
    ($columns | Where-Object Name -eq 'Name').Name = 'UnsafeRename'
    $rejected = $false
    try { Save-Design $before $columns } catch { $rejected = $true }
    Assert-Equal $rejected $true 'SQLite dependent rename safely rejected'
    $editor.DropTable($profile, $node)
    Assert-Equal (Read-Value "SELECT COUNT(*) FROM sqlite_master WHERE name='DesignProbe'") 0 'SQLite table deletion'

    Run-Sql 'CREATE TABLE DesignProbe (Id INTEGER PRIMARY KEY, Code TEXT COLLATE NOCASE UNIQUE, Qty INTEGER CHECK(Qty>=0), Legacy TEXT, CONSTRAINT CK_Code CHECK(length(Code)>0))'
    Run-Sql "INSERT INTO DesignProbe VALUES (1,'Alpha',3,'old')"
    Run-Sql 'CREATE VIEW V_Probe AS SELECT Code FROM DesignProbe'
    $before = $editor.Load($profile, $node)
    $columns = Copy-Columns $before
    ($columns | Where-Object Name -eq 'Qty').AllowNull = $false
    $extra = New-Column 'Added' 'TEXT'; $columns.Add($extra)
    Save-Design $before $columns
    Assert-Equal (Read-Value 'SELECT Code FROM V_Probe') 'Alpha' 'SQLite dependent view survives rebuild'
    $rejected = $false
    try { Run-Sql "INSERT INTO DesignProbe (Code, Qty) VALUES ('alpha',2)" } catch { $rejected = $true }
    Assert-Equal $rejected $true 'SQLite UNIQUE and NOCASE collation preserved'
    $rejected = $false
    try { Run-Sql "UPDATE DesignProbe SET Qty=-1 WHERE Id=1" } catch { $rejected = $true }
    Assert-Equal $rejected $true 'SQLite CHECK preserved'
    $rejected = $false
    try { Run-Sql "UPDATE DesignProbe SET Code='' WHERE Id=1" } catch { $rejected = $true }
    Assert-Equal $rejected $true 'SQLite table CHECK preserved'
    Run-Sql 'CREATE TABLE Child (Id INTEGER PRIMARY KEY, ParentId INTEGER REFERENCES DesignProbe(Id) ON DELETE CASCADE)'
    Run-Sql 'INSERT INTO Child VALUES (1,1)'
    $rejected = $false
    try { $editor.DropTable($profile, $node) } catch { $rejected = $true }
    Assert-Equal $rejected $true 'SQLite DROP does not cascade-delete child rows'
    Assert-Equal (Read-Value 'SELECT COUNT(*) FROM Child') 1 'SQLite child rows retained'
    $before = $editor.Load($profile, $node)
    $columns = Copy-Columns $before
    [void]$columns.Remove(($columns | Where-Object Name -eq 'Legacy'))
    Save-Design $before $columns
    Assert-Equal (Read-Value 'SELECT COUNT(*) FROM Child') 1 'SQLite rebuild retains child references'
    $before = $editor.Load($profile, $node)
    $columns = Copy-Columns $before
    ($columns | Where-Object Name -eq 'Added').AllowNull = $false
    $rejected = $false
    try { Save-Design $before $columns } catch { $rejected = $true }
    Assert-Equal $rejected $true 'SQLite invalid NULL conversion rejected'
    Assert-Equal (Read-Value 'SELECT Code FROM DesignProbe WHERE Id=1') 'Alpha' 'SQLite failed rebuild rolled back'
    Run-Sql 'DROP VIEW V_Probe'
    Run-Sql 'DROP TABLE Child'
    $editor.DropTable($profile, $node)

    Run-Sql 'CREATE TABLE DesignProbe (A INTEGER, B TEXT, PRIMARY KEY(A,B))'
    Run-Sql "INSERT INTO DesignProbe VALUES (1,'key')"
    $before = $editor.Load($profile, $node)
    $columns = Copy-Columns $before
    foreach ($column in $columns) { $column.IsPrimaryKey = $false; $column.AllowNull = $true }
    ($columns | Where-Object Name -eq 'B').IsPrimaryKey = $true
    Save-Design $before $columns
    Assert-Equal @($editor.Load($profile, $node).Columns | Where-Object IsPrimaryKey).Count 1 'SQLite composite primary key changed'
    Assert-Equal (Read-Value 'SELECT A FROM DesignProbe') 1 'SQLite key change preserves rows'
    $editor.DropTable($profile, $node)
    Write-Output ('PASS: ' + $checks + ' real database assertions')
}
finally {
    [Data.OleDb.OleDbConnection]::ReleaseObjectPool()
    [GC]::Collect(); [GC]::WaitForPendingFinalizers()
    foreach ($file in Get-ChildItem -LiteralPath $probeRoot -File) { Remove-Item -LiteralPath $file.FullName -Force }
    Remove-Item -LiteralPath $probeRoot -Force
}
