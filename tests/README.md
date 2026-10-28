# 表结构编辑验证

先构建 `TXDB.sln`，再从仓库根目录使用 Windows PowerShell（x86）运行：

```powershell
& "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -STA -ExecutionPolicy Bypass -File .\tests\TableSchemaEditor.Probe.ps1
& "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -STA -ExecutionPolicy Bypass -File .\tests\TableSchemaSql.Probe.ps1
& "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -STA -ExecutionPolicy Bypass -File .\tests\TableStructureView.Probe.ps1
& "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -STA -ExecutionPolicy Bypass -File .\tests\TablePreview.Probe.ps1
```

- 数据库验证创建临时 Access/SQLite 数据库，覆盖建表、字段修改、默认值、可空、主键、依赖保留和失败恢复；完成后删除临时数据库。Access 验证需要 Jet 4.0、ADOX 和 DAO 3.6。
- SQL 验证检查 SQL Server、MySQL、Oracle 的语句生成和目标数据库选择，不访问外部数据库。
- 界面验证在内存中加载 WPF，检查按钮状态及勾选逻辑，输出临时 PNG 预览，不打开配置中的连接。
- 数据预览验证使用临时 SQLite/Access 数据库，检查空表列头、新增、多行选择与删除、无主键批量删除回滚及编辑按钮状态，并渲染左侧行选择栏。

SQL Server、MySQL、Oracle 仍需在对应数据库环境中集成验证。修改现有自增属性、计算字段，以及涉及复杂依赖的字段删除/重命名，会由设计器或数据库拒绝，保留已有规则。
