# Database-independent dialect validation. No network/database connections.
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'TXDB\bin\Debug\TXDB.exe'))
$checks=0
function Check([bool]$condition,[string]$message) { if (!$condition) { throw $message }; $script:checks++ }
function New-Column([string]$name,[string]$type,[string]$typeArguments='') {
    $column=New-Object TXDB.Models.TableColumnDefinition
    $column.Name=$name; $column.TypeName=$type; $column.TypeArguments=$typeArguments
    return $column
}
function Build-Sql($editor,$node,$before,$columns) {
    $method=$editor.GetType().GetMethod('BuildStatements',[Reflection.BindingFlags]'Instance,NonPublic')
    $action=$method.CreateDelegate([Func[TXDB.Models.DatabaseObjectNode,TXDB.Data.TableSchemaSnapshot,System.Collections.Generic.IList[TXDB.Models.TableColumnDefinition],System.Collections.Generic.List[string]]],$editor)
    return ,$action.Invoke($node,$before,$columns)
}
$node=New-Object TXDB.Models.DatabaseObjectNode
$node.Name='Products'; $node.DatabaseName='sample'; $node.SchemaName='dbo'; $node.Kind=[TXDB.Models.DatabaseObjectKind]::Table
$editor=New-Object TXDB.Data.TableSchemaEditor((New-Object TXDB.Data.SqlServerDatabaseProvider))
$profile=New-Object TXDB.Models.ConnectionProfile
$profile.RawConnectionString='Data Source=localhost;Initial Catalog=original;Integrated Security=True'
$scoped=$editor.GetScopedProfile($profile,$node)
Check ((New-Object Data.SqlClient.SqlConnectionStringBuilder($scoped.RawConnectionString)).InitialCatalog -eq 'sample') 'SQL Server raw connection string targets selected database'
$before=New-Object TXDB.Data.TableSchemaSnapshot
$column=New-Column 'Name' 'nvarchar' '50'; $column.OriginalName='Name'; $column.DefaultSql="('old')"; $column.DefaultConstraintName='DF_Name'
$before.Columns.Add($column)
$columns=New-Object 'System.Collections.Generic.List[TXDB.Models.TableColumnDefinition]'
$changed=$column.Clone(); $changed.Name='Label'; $changed.TypeArguments='80'; $changed.DefaultSql="'new'"; $changed.Comment='description'; $columns.Add($changed)
$sql=Build-Sql $editor $node $before $columns
Check (($sql -join ' ') -match 'sp_rename N') 'SQL Server uses Unicode rename parameters'
Check (($sql -join ' ') -match 'DROP CONSTRAINT \[DF_Name\]') 'SQL Server removes named default before altering'
Check (($sql -join ' ') -match 'ALTER COLUMN \[Label\] nvarchar\(80\)') 'SQL Server alters type with dialect syntax'
Check (($sql -join ' ') -match "ADD DEFAULT 'new' FOR \[Label\]") 'SQL Server recreates default'
$added=New-Column 'Count' 'int'; $columns.Add($added)
$sql=Build-Sql $editor $node $before $columns
Check (($sql -join ' ') -notmatch 'ADD COLUMN') 'SQL Server ADD does not use COLUMN keyword'

$editor=New-Object TXDB.Data.TableSchemaEditor((New-Object TXDB.Data.MySqlDatabaseProvider))
$profile.RawConnectionString='Server=localhost;Database=original;User ID=demo'
$scoped=$editor.GetScopedProfile($profile,$node)
Check ($scoped.RawConnectionString -match 'Database=sample') 'MySQL raw connection string targets selected database'
$node.SchemaName='sample'
$before=New-Object TXDB.Data.TableSchemaSnapshot
$column=New-Column 'Id' 'int'; $column.OriginalName='Id'; $column.IsPrimaryKey=$true; $column.IsIdentity=$true; $before.Columns.Add($column)
$columns=New-Object 'System.Collections.Generic.List[TXDB.Models.TableColumnDefinition]'
$changed=$column.Clone(); $changed.Name='ProductId'; $columns.Add($changed)
$added=New-Column 'Part' 'int'; $added.IsPrimaryKey=$true; $columns.Add($added)
$sql=Build-Sql $editor $node $before $columns
Check ($sql.Count -eq 1) 'MySQL key and identity changes share one ALTER TABLE'
Check ($sql[0] -match 'DROP PRIMARY KEY.*CHANGE COLUMN.*AUTO_INCREMENT.*ADD PRIMARY KEY') 'MySQL avoids invalid intermediate identity state'
$enum=New-Column 'State' 'enum' "'a','b,c'"
$columns=New-Object 'System.Collections.Generic.List[TXDB.Models.TableColumnDefinition]'; $columns.Add($enum)
$sql=Build-Sql $editor $node $null $columns
Check ($sql[0].Contains("enum('a','b,c')")) 'MySQL ENUM values preserve quoted comma'

$editor=New-Object TXDB.Data.TableSchemaEditor((New-Object TXDB.Data.OracleDatabaseProvider))
$node.SchemaName='DEMO'
$columns=New-Object 'System.Collections.Generic.List[TXDB.Models.TableColumnDefinition]'
$column=New-Column 'Name' 'VARCHAR2' '80'; $column.DefaultSql="'ready'"; $column.AllowNull=$false; $columns.Add($column)
$sql=Build-Sql $editor $node $null $columns
Check ($sql[0] -match "VARCHAR2\(80\) DEFAULT 'ready' NOT NULL") 'Oracle DEFAULT precedes NOT NULL'
$before=New-Object TXDB.Data.TableSchemaSnapshot
$column.OriginalName='Name'; $before.Columns.Add($column.Clone()); $columns[0].TypeArguments='100'
$sql=Build-Sql $editor $node $before $columns
Check (($sql -join ' ') -match 'MODIFY \("Name" VARCHAR2\(100\)\)') 'Oracle type-only change does not redundantly set NOT NULL'
$columns[0].DefaultSql="'a b'"
$profile=New-Object TXDB.Models.ConnectionProfile
$editor.Validate($node,$columns,$before)
$checks++
$columns[0].DefaultSql='0); DROP TABLE Products; --'
$rejected=$false
try { $editor.Validate($node,$columns,$before) } catch { $rejected=$true }
Check $rejected 'Default expression cannot append statements'
Write-Output ('PASS: '+$checks+' dialect and validation assertions')
