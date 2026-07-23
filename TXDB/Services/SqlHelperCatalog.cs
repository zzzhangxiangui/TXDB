using System.Collections.Generic;
using TXDB.Models;

namespace TXDB.Services
{
    /// <summary>
    /// 提供 SQL 快速插入模板和关键字帮助说明。
    /// </summary>
    public static class SqlHelperCatalog
    {
        /// <summary>
        /// 获取常用 SQL 快速插入模板。
        /// </summary>
        public static IList<SqlTemplateInfo> GetTemplateItems()
        {
            return new List<SqlTemplateInfo>
            {
                new SqlTemplateInfo("查询全部", "SELECT * FROM 表名"),
                new SqlTemplateInfo("条件查询", "SELECT 字段1, 字段2 FROM 表名 WHERE 条件"),
                new SqlTemplateInfo("去重查询", "SELECT DISTINCT 字段1, 字段2\r\nFROM 表名"),
                new SqlTemplateInfo("排序查询", "SELECT * FROM 表名 ORDER BY 字段名 ASC"),
                new SqlTemplateInfo("分组统计", "SELECT 字段名, COUNT(*) AS 数量 FROM 表名 GROUP BY 字段名"),
                new SqlTemplateInfo("重复值统计", "SELECT 字段名, COUNT(*) AS 数量\r\nFROM 表名\r\nGROUP BY 字段名\r\nHAVING COUNT(*) > 1"),
                new SqlTemplateInfo("聚合汇总", "SELECT 分组字段,\r\n       COUNT(*) AS 数量,\r\n       SUM(金额字段) AS 合计金额,\r\n       AVG(金额字段) AS 平均金额,\r\n       MAX(时间字段) AS 最新时间\r\nFROM 表名\r\nGROUP BY 分组字段"),
                new SqlTemplateInfo("左连接", "SELECT a.*, b.*\r\nFROM 表A a\r\nLEFT JOIN 表B b ON a.关联字段 = b.关联字段"),
                new SqlTemplateInfo("内连接", "SELECT a.*, b.*\r\nFROM 表A a\r\nINNER JOIN 表B b ON a.关联字段 = b.关联字段"),
                new SqlTemplateInfo("子查询", "SELECT *\r\nFROM 表名\r\nWHERE 字段名 IN (\r\n    SELECT 字段名 FROM 关联表 WHERE 条件\r\n)"),
                new SqlTemplateInfo("存在判断", "SELECT *\r\nFROM 表A a\r\nWHERE EXISTS (\r\n    SELECT 1 FROM 表B b WHERE b.关联字段 = a.关联字段\r\n)"),
                new SqlTemplateInfo("合并结果", "SELECT 字段1, 字段2 FROM 表A WHERE 条件\r\nUNION ALL\r\nSELECT 字段1, 字段2 FROM 表B WHERE 条件"),
                new SqlTemplateInfo("CTE 临时结果", "WITH 临时结果 AS (\r\n    SELECT 字段1, 字段2 FROM 表名 WHERE 条件\r\n)\r\nSELECT * FROM 临时结果"),
                new SqlTemplateInfo("空值替换", "SELECT COALESCE(字段名, '默认值') AS 字段别名\r\nFROM 表名"),
                new SqlTemplateInfo("条件显示", "SELECT CASE WHEN 条件 THEN '满足' ELSE '不满足' END AS 显示字段\r\nFROM 表名"),
                new SqlTemplateInfo("新增数据", "INSERT INTO 表名 (字段1, 字段2) VALUES (值1, 值2)"),
                new SqlTemplateInfo("批量新增", "INSERT INTO 表名 (字段1, 字段2)\r\nSELECT 字段1, 字段2 FROM 来源表 WHERE 条件"),
                new SqlTemplateInfo("查后新增", "INSERT INTO 目标表 (字段1, 字段2)\r\nSELECT 字段1, 字段2\r\nFROM 来源表\r\nWHERE 条件"),
                new SqlTemplateInfo("更新数据", "UPDATE 表名 SET 字段名 = 值 WHERE 条件"),
                new SqlTemplateInfo("更新前预查", "SELECT * FROM 表名 WHERE 条件"),
                new SqlTemplateInfo("批量更新 CASE", "UPDATE 表名\r\nSET 字段名 = CASE\r\n    WHEN 条件1 THEN 值1\r\n    WHEN 条件2 THEN 值2\r\n    ELSE 字段名\r\nEND\r\nWHERE 条件1 OR 条件2"),
                new SqlTemplateInfo("删除数据", "DELETE FROM 表名 WHERE 条件"),
                new SqlTemplateInfo("删除前预查", "SELECT * FROM 表名 WHERE 条件"),
                new SqlTemplateInfo("表结构空查", "SELECT * FROM 表名 WHERE 1 = 0"),
                new SqlTemplateInfo("创建表", "CREATE TABLE 表名 (\r\n    ID INT NOT NULL,\r\n    NAME VARCHAR(100),\r\n    CREATE_TIME DATETIME,\r\n    PRIMARY KEY (ID)\r\n)"),
                new SqlTemplateInfo("添加字段", "ALTER TABLE 表名 ADD 字段名 VARCHAR(100)"),
                new SqlTemplateInfo("修改字段", "ALTER TABLE 表名 ALTER COLUMN 字段名 VARCHAR(200)"),
                new SqlTemplateInfo("删除字段", "ALTER TABLE 表名 DROP COLUMN 字段名"),
                new SqlTemplateInfo("创建普通索引", "CREATE INDEX IX_表名_字段名 ON 表名 (字段名)"),
                new SqlTemplateInfo("创建唯一索引", "CREATE UNIQUE INDEX UX_表名_字段名 ON 表名 (字段名)"),
                new SqlTemplateInfo("删除索引", "DROP INDEX 索引名"),
                new SqlTemplateInfo("创建视图", "CREATE VIEW 视图名 AS\r\nSELECT 字段1, 字段2\r\nFROM 表名\r\nWHERE 条件"),
                new SqlTemplateInfo("删除视图", "DROP VIEW 视图名"),
                new SqlTemplateInfo("创建触发器", "CREATE TRIGGER 触发器名\r\nON 表名\r\nAFTER INSERT\r\nAS\r\nBEGIN\r\n    -- 在这里编写触发逻辑\r\n    SELECT * FROM inserted\r\nEND"),
                new SqlTemplateInfo("删除触发器", "DROP TRIGGER 触发器名"),
                new SqlTemplateInfo("创建存储过程", "CREATE PROCEDURE 存储过程名\r\nAS\r\nBEGIN\r\n    SELECT * FROM 表名\r\nEND"),
                new SqlTemplateInfo("执行存储过程", "EXEC 存储过程名"),
                new SqlTemplateInfo("事务模板", "BEGIN TRANSACTION\r\n\r\n-- 在这里编写需要一起提交的 SQL\r\n\r\nCOMMIT TRANSACTION"),
                new SqlTemplateInfo("回滚事务", "ROLLBACK TRANSACTION"),
                new SqlTemplateInfo("SQL Server 前100条", "SELECT TOP 100 *\r\nFROM 表名\r\nORDER BY 排序字段 DESC"),
                new SqlTemplateInfo("Access 前100条", "SELECT TOP 100 *\r\nFROM 表名\r\nORDER BY 排序字段 DESC"),
                new SqlTemplateInfo("SQL Server 分页", "SELECT *\r\nFROM 表名\r\nORDER BY 排序字段\r\nOFFSET 0 ROWS FETCH NEXT 100 ROWS ONLY"),
                new SqlTemplateInfo("MySQL 前100条", "SELECT *\r\nFROM 表名\r\nORDER BY 排序字段 DESC\r\nLIMIT 100"),
                new SqlTemplateInfo("SQLite 前100条", "SELECT *\r\nFROM 表名\r\nORDER BY 排序字段 DESC\r\nLIMIT 100"),
                new SqlTemplateInfo("MySQL/SQLite 分页", "SELECT *\r\nFROM 表名\r\nORDER BY 排序字段\r\nLIMIT 100 OFFSET 0"),
                new SqlTemplateInfo("Oracle 前100条", "SELECT *\r\nFROM 表名\r\nWHERE ROWNUM <= 100"),
                new SqlTemplateInfo("Oracle 分页", "SELECT *\r\nFROM (\r\n    SELECT t.*, ROWNUM rn\r\n    FROM 表名 t\r\n    WHERE ROWNUM <= 200\r\n)\r\nWHERE rn > 100")
            };
        }

        /// <summary>
        /// 获取 SQL 关键字中文说明、语法和示例。
        /// </summary>
        public static IList<SqlKeywordHelpInfo> GetKeywordHelpItems()
        {
            return new List<SqlKeywordHelpInfo>
            {
                new SqlKeywordHelpInfo("SELECT", "查询字段", "从表、视图或子查询中取出需要查看的数据列。", "SELECT 字段1, 字段2 FROM 表名", "SELECT ID, NAME FROM METER_INFO", "使用 * 可以查询所有字段，但正式排查时建议写出字段名，结果更清楚。"),
                new SqlKeywordHelpInfo("FROM", "数据来源", "指定查询数据来自哪张表、视图或子查询。", "SELECT * FROM 表名", "SELECT * FROM METER_INFO", "表名包含特殊字符时需要按数据库类型加括号、双引号或反引号。"),
                new SqlKeywordHelpInfo("WHERE", "筛选条件", "在查询、更新或删除时限定数据范围。", "WHERE 字段名 = 值", "SELECT * FROM METER_INFO WHERE STATUS = 1", "UPDATE 和 DELETE 通常必须带 WHERE，避免误操作全表。"),
                new SqlKeywordHelpInfo("DISTINCT", "去重", "去掉查询结果中完全重复的行。", "SELECT DISTINCT 字段1, 字段2 FROM 表名", "SELECT DISTINCT STATUS FROM METER_INFO", "DISTINCT 会按 SELECT 出来的所有字段一起判断是否重复。"),
                new SqlKeywordHelpInfo("ORDER BY", "结果排序", "按一个或多个字段排序查询结果。", "ORDER BY 字段名 ASC|DESC", "SELECT * FROM METER_INFO ORDER BY CREATE_TIME DESC", "ASC 表示升序，DESC 表示降序。"),
                new SqlKeywordHelpInfo("GROUP BY", "分组统计", "把相同字段值的数据分成一组，常配合聚合函数。", "GROUP BY 字段名", "SELECT STATUS, COUNT(*) FROM METER_INFO GROUP BY STATUS", "SELECT 中非聚合字段一般要出现在 GROUP BY 中。"),
                new SqlKeywordHelpInfo("HAVING", "分组筛选", "对 GROUP BY 之后的聚合结果继续筛选。", "HAVING COUNT(*) > 数量", "SELECT STATUS, COUNT(*) FROM METER_INFO GROUP BY STATUS HAVING COUNT(*) > 10", "WHERE 发生在分组前，HAVING 发生在分组后。"),
                new SqlKeywordHelpInfo("JOIN", "关联表", "把两张表按条件关联起来查询。", "FROM 表A a JOIN 表B b ON a.ID = b.A_ID", "SELECT a.ID, b.NAME FROM A a JOIN B b ON a.ID = b.A_ID", "JOIN 默认通常表示 INNER JOIN，只返回两边都匹配的数据。"),
                new SqlKeywordHelpInfo("LEFT JOIN", "左连接", "保留左表所有记录，右表匹配不到时右表字段为空。", "FROM 表A a LEFT JOIN 表B b ON a.ID = b.A_ID", "SELECT a.ID, b.NAME FROM A a LEFT JOIN B b ON a.ID = b.A_ID", "适合查主表数据以及可选的明细或配置。"),
                new SqlKeywordHelpInfo("INNER JOIN", "内连接", "只返回两张表都能匹配上的记录。", "FROM 表A a INNER JOIN 表B b ON a.ID = b.A_ID", "SELECT a.ID, b.NAME FROM A a INNER JOIN B b ON a.ID = b.A_ID", "如果需要保留主表未匹配数据，改用 LEFT JOIN。"),
                new SqlKeywordHelpInfo("EXISTS", "存在判断", "判断子查询是否至少返回一行，常用于关联过滤。", "WHERE EXISTS (SELECT 1 FROM 表B WHERE 表B.字段 = 表A.字段)", "SELECT * FROM A a WHERE EXISTS (SELECT 1 FROM B b WHERE b.A_ID = a.ID)", "EXISTS 只关心有没有记录，不关心子查询 SELECT 出什么字段。"),
                new SqlKeywordHelpInfo("NOT EXISTS", "不存在判断", "筛选子查询找不到匹配记录的数据。", "WHERE NOT EXISTS (SELECT 1 FROM 表B WHERE 表B.字段 = 表A.字段)", "SELECT * FROM A a WHERE NOT EXISTS (SELECT 1 FROM B b WHERE b.A_ID = a.ID)", "适合找未配置、未关联、未处理的数据。"),
                new SqlKeywordHelpInfo("UNION", "合并并去重", "把多个查询结果合并成一个结果集，并去掉重复行。", "查询1 UNION 查询2", "SELECT CODE FROM A UNION SELECT CODE FROM B", "两边查询的字段数量和兼容类型要一致。"),
                new SqlKeywordHelpInfo("UNION ALL", "合并不去重", "把多个查询结果直接拼接到一起，不去重。", "查询1 UNION ALL 查询2", "SELECT CODE FROM A UNION ALL SELECT CODE FROM B", "通常比 UNION 更快，适合允许重复或确定不会重复的场景。"),
                new SqlKeywordHelpInfo("WITH", "公共表表达式", "先定义一个临时查询结果，再在后续 SQL 中引用。", "WITH 名称 AS (SELECT ...) SELECT * FROM 名称", "WITH T AS (SELECT * FROM METER_INFO WHERE STATUS = 1) SELECT * FROM T", "旧版本 MySQL 不支持 WITH；Access 对这类语法支持有限。"),
                new SqlKeywordHelpInfo("INSERT INTO", "新增数据", "向表里新增一行或多行数据。", "INSERT INTO 表名 (字段1, 字段2) VALUES (值1, 值2)", "INSERT INTO METER_INFO (ID, NAME) VALUES (1, '测试')", "字段顺序必须和值顺序一致。"),
                new SqlKeywordHelpInfo("UPDATE", "更新数据", "修改表中已有记录的字段值。", "UPDATE 表名 SET 字段名 = 值 WHERE 条件", "UPDATE METER_INFO SET STATUS = 1 WHERE ID = 100", "强烈建议先 SELECT 确认 WHERE 命中的数据。"),
                new SqlKeywordHelpInfo("DELETE FROM", "删除数据", "删除满足条件的记录。", "DELETE FROM 表名 WHERE 条件", "DELETE FROM METER_INFO WHERE ID = 100", "删除前先备份或先 SELECT 验证条件。"),
                new SqlKeywordHelpInfo("CREATE TABLE", "创建表", "创建一张新表并定义字段和约束。", "CREATE TABLE 表名 (字段 类型 约束)", "CREATE TABLE DEMO (ID INT NOT NULL, NAME VARCHAR(100), PRIMARY KEY (ID))", "不同数据库字段类型略有差异，需要按实际数据库调整。"),
                new SqlKeywordHelpInfo("ALTER TABLE", "修改表", "调整表结构，例如新增字段、删除字段或修改字段类型。", "ALTER TABLE 表名 ADD 字段名 类型", "ALTER TABLE METER_INFO ADD REMARK VARCHAR(200)", "不同数据库修改字段语法差别较大，执行前建议确认方言。"),
                new SqlKeywordHelpInfo("DROP TABLE", "删除表", "删除整张表及其数据。", "DROP TABLE 表名", "DROP TABLE DEMO", "这是高风险操作，执行前必须确认备份和环境。"),
                new SqlKeywordHelpInfo("CREATE INDEX", "创建索引", "为字段创建索引，提高按该字段查询或排序的速度。", "CREATE INDEX 索引名 ON 表名 (字段名)", "CREATE INDEX IX_METER_INFO_BAR_CODE ON METER_INFO (MD_BAR_CODE)", "索引会提升查询，也会增加写入成本和存储空间。"),
                new SqlKeywordHelpInfo("CREATE UNIQUE INDEX", "创建唯一索引", "创建唯一约束能力的索引，防止字段值重复。", "CREATE UNIQUE INDEX 索引名 ON 表名 (字段名)", "CREATE UNIQUE INDEX UX_USER_CODE ON SYS_USER (USER_CODE)", "已有重复数据时创建会失败。"),
                new SqlKeywordHelpInfo("DROP INDEX", "删除索引", "删除不再需要的索引。", "DROP INDEX 索引名", "DROP INDEX IX_METER_INFO_BAR_CODE", "不同数据库删除索引是否需要表名不同，执行前确认方言。"),
                new SqlKeywordHelpInfo("CREATE VIEW", "创建视图", "把一段查询保存成数据库对象，方便后续直接查询。", "CREATE VIEW 视图名 AS SELECT ...", "CREATE VIEW V_METER AS SELECT ID, NAME FROM METER_INFO", "视图通常不保存数据，只保存查询定义。"),
                new SqlKeywordHelpInfo("CREATE TRIGGER", "创建触发器", "在表发生 INSERT、UPDATE 或 DELETE 时自动执行逻辑。", "CREATE TRIGGER 触发器名 ON 表名 AFTER INSERT AS BEGIN ... END", "CREATE TRIGGER TR_DEMO ON DEMO AFTER INSERT AS BEGIN SELECT * FROM inserted END", "触发器容易影响写入性能和调试复杂度，建议用途明确再创建。"),
                new SqlKeywordHelpInfo("BEGIN TRANSACTION", "开始事务", "把多条 SQL 放在一个事务中，要么全部成功，要么全部回滚。", "BEGIN TRANSACTION ... COMMIT TRANSACTION", "BEGIN TRANSACTION\r\nUPDATE A SET NAME = 'X' WHERE ID = 1\r\nCOMMIT TRANSACTION", "不同数据库事务语法略有不同，MySQL/SQLite 常用 BEGIN 或 START TRANSACTION。"),
                new SqlKeywordHelpInfo("COMMIT", "提交事务", "确认事务中的修改并永久生效。", "COMMIT 或 COMMIT TRANSACTION", "COMMIT TRANSACTION", "提交后通常不能再通过事务回滚。"),
                new SqlKeywordHelpInfo("ROLLBACK", "回滚事务", "撤销当前事务中尚未提交的修改。", "ROLLBACK 或 ROLLBACK TRANSACTION", "ROLLBACK TRANSACTION", "发生异常或数据不符合预期时使用。"),
                new SqlKeywordHelpInfo("LIKE", "模糊匹配", "按字符串模式匹配字段值。", "字段 LIKE '%关键字%'", "SELECT * FROM METER_INFO WHERE NAME LIKE '%测试%'", "% 表示任意长度字符，_ 表示单个字符。"),
                new SqlKeywordHelpInfo("IN", "集合匹配", "判断字段值是否在一组候选值中。", "字段 IN (值1, 值2)", "SELECT * FROM METER_INFO WHERE STATUS IN (1, 2)", "候选值很多时要注意执行计划和参数化。"),
                new SqlKeywordHelpInfo("BETWEEN", "区间匹配", "判断字段值是否在两个边界之间。", "字段 BETWEEN 起始值 AND 结束值", "SELECT * FROM METER_INFO WHERE CREATE_TIME BETWEEN '2026-01-01' AND '2026-01-31'", "多数数据库 BETWEEN 包含边界值。"),
                new SqlKeywordHelpInfo("IS NULL", "为空判断", "判断字段是否没有值。", "字段 IS NULL", "SELECT * FROM METER_INFO WHERE REMARK IS NULL", "NULL 不能用 = 判断。"),
                new SqlKeywordHelpInfo("IS NOT NULL", "非空判断", "判断字段存在实际值。", "字段 IS NOT NULL", "SELECT * FROM METER_INFO WHERE REMARK IS NOT NULL", "空字符串和 NULL 不是一回事，Access 和 Oracle 在部分场景下表现会有差异。"),
                new SqlKeywordHelpInfo("COALESCE", "空值替换", "返回参数列表中第一个非 NULL 的值。", "COALESCE(字段名, 默认值)", "SELECT COALESCE(REMARK, '无备注') FROM METER_INFO", "SQL Server 也常见 ISNULL，MySQL 常见 IFNULL，Oracle 常见 NVL。"),
                new SqlKeywordHelpInfo("NULLIF", "相等转空", "如果两个表达式相等就返回 NULL，否则返回第一个表达式。", "NULLIF(表达式1, 表达式2)", "SELECT NULLIF(RESULT_CODE, '') FROM METER_INFO", "常用于把空字符串、0 或特殊值转成 NULL 后再处理。"),
                new SqlKeywordHelpInfo("CAST", "类型转换", "把字段或表达式转换成指定类型。", "CAST(字段名 AS 目标类型)", "SELECT CAST(ID AS VARCHAR(50)) FROM METER_INFO", "不同数据库可用类型名称不完全一致。"),
                new SqlKeywordHelpInfo("CONVERT", "类型转换", "SQL Server 常用的类型转换函数。", "CONVERT(目标类型, 表达式)", "SELECT CONVERT(VARCHAR(19), CREATE_TIME, 120) FROM METER_INFO", "主要是 SQL Server 方言；其他数据库通常用 CAST 或各自函数。"),
                new SqlKeywordHelpInfo("COUNT", "统计数量", "统计结果行数或非空字段数量。", "COUNT(*) 或 COUNT(字段名)", "SELECT COUNT(*) FROM METER_INFO", "COUNT(*) 表示统计行数，COUNT(字段) 通常不统计 NULL。"),
                new SqlKeywordHelpInfo("COUNT DISTINCT", "去重计数", "统计某个字段去重后的数量。", "COUNT(DISTINCT 字段名)", "SELECT COUNT(DISTINCT MD_BAR_CODE) FROM METER_INFO", "多个字段组合去重的写法在不同数据库中差异较大。"),
                new SqlKeywordHelpInfo("SUM", "求和", "对数值字段求和。", "SUM(字段名)", "SELECT SUM(AMOUNT) FROM ORDER_INFO", "字段必须是数值或可转换为数值。"),
                new SqlKeywordHelpInfo("AVG", "平均值", "计算数值字段的平均值。", "AVG(字段名)", "SELECT AVG(AMOUNT) FROM ORDER_INFO", "NULL 通常不会参与平均值计算。"),
                new SqlKeywordHelpInfo("MAX", "最大值", "返回字段中的最大值。", "MAX(字段名)", "SELECT MAX(CREATE_TIME) FROM METER_INFO", "常用来找最新时间、最大编号或最高金额。"),
                new SqlKeywordHelpInfo("MIN", "最小值", "返回字段中的最小值。", "MIN(字段名)", "SELECT MIN(CREATE_TIME) FROM METER_INFO", "常用来找最早时间、最小编号或最低金额。"),
                new SqlKeywordHelpInfo("CASE WHEN", "条件表达式", "按条件返回不同值，类似程序里的 if else。", "CASE WHEN 条件 THEN 值1 ELSE 值2 END", "SELECT CASE WHEN STATUS = 1 THEN '启用' ELSE '停用' END AS 状态 FROM METER_INFO", "适合把编码字段转成可读文本。"),
                new SqlKeywordHelpInfo("TOP", "前 N 行", "SQL Server 和 Access 常用的限制返回行数语法。", "SELECT TOP 100 * FROM 表名", "SELECT TOP 100 * FROM METER_INFO ORDER BY CREATE_TIME DESC", "MySQL 和 SQLite 用 LIMIT，Oracle 常用 ROWNUM 或 FETCH FIRST。"),
                new SqlKeywordHelpInfo("LIMIT", "限制行数", "MySQL 和 SQLite 常用的限制返回行数语法。", "LIMIT 数量 OFFSET 偏移量", "SELECT * FROM METER_INFO ORDER BY CREATE_TIME DESC LIMIT 100 OFFSET 0", "SQL Server 用 TOP 或 OFFSET FETCH，Oracle 用 ROWNUM 或 FETCH FIRST。"),
                new SqlKeywordHelpInfo("OFFSET FETCH", "分页", "SQL Server 和部分数据库支持的分页语法。", "ORDER BY 字段 OFFSET 起始行 ROWS FETCH NEXT 数量 ROWS ONLY", "SELECT * FROM METER_INFO ORDER BY ID OFFSET 0 ROWS FETCH NEXT 100 ROWS ONLY", "必须配合 ORDER BY，否则分页顺序不稳定。"),
                new SqlKeywordHelpInfo("ROWNUM", "Oracle 行号", "Oracle 中用于限制或辅助分页的伪列。", "WHERE ROWNUM <= 数量", "SELECT * FROM METER_INFO WHERE ROWNUM <= 100", "复杂分页通常需要子查询包一层。")
            };
        }
    }
}
