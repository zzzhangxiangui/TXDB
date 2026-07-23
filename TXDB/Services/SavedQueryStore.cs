using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TXDB.Models;

namespace TXDB.Services
{
    /// <summary>
    /// 负责保存查询语句的本地读取、保存和删除。
    /// </summary>
    public class SavedQueryStore
    {
        private readonly string rootFolder;
        private readonly string legacyFilePath;

        /// <summary>
        /// 初始化保存查询存储服务。
        /// </summary>
        public SavedQueryStore()
        {
            rootFolder = AppStoragePaths.QueriesFolder;
            legacyFilePath = AppStoragePaths.LegacySavedQueriesFile;
        }

        /// <summary>
        /// 从本地文件读取全部保存查询。
        /// </summary>
        public List<SavedQueryInfo> Load()
        {
            List<SavedQueryInfo> queries = new List<SavedQueryInfo>();
            if (Directory.Exists(rootFolder))
            {
                foreach (string fileName in Directory.GetFiles(rootFolder, "*.json", SearchOption.AllDirectories))
                {
                    queries.AddRange(ReadQueryFile(fileName));
                }
            }

            if (queries.Count == 0 && File.Exists(legacyFilePath))
            {
                queries.AddRange(LoadLegacyQueries());
                Save(queries);
            }

            return queries
                .Where(item => item != null)
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
                .OrderBy(item => item.ConnectionName)
                .ThenBy(item => item.DatabaseName)
                .ThenBy(item => item.SchemaName)
                .ThenBy(item => item.Title)
                .ToList();
        }

        /// <summary>
        /// 保存全部查询语句到本地文件。
        /// </summary>
        public void Save(IEnumerable<SavedQueryInfo> queries)
        {
            List<SavedQueryInfo> saveQueries = queries == null ? new List<SavedQueryInfo>() : queries.ToList();
            AppStoragePaths.EnsureFolder(rootFolder);
            ClearExistingJsonFiles();

            foreach (IGrouping<string, SavedQueryInfo> connectionGroup in saveQueries.GroupBy(item => NormalizeKey(item.ConnectionId)))
            {
                foreach (IGrouping<string, SavedQueryInfo> databaseGroup in connectionGroup.GroupBy(item => NormalizeKey(item.DatabaseName)))
                {
                    string fileName = GetQueryFilePath(connectionGroup.Key, databaseGroup.Key);
                    AppStoragePaths.EnsureFolder(Path.GetDirectoryName(fileName));
                    List<SavedQueryInfo> databaseQueries = databaseGroup
                        .OrderBy(item => item.SchemaName)
                        .ThenBy(item => item.Title)
                        .ToList();
                    File.WriteAllText(fileName, JsonConvert.SerializeObject(databaseQueries, Formatting.Indented), Encoding.UTF8);
                }
            }
        }

        /// <summary>
        /// 保存或覆盖同一连接、数据库、架构和标题下的查询。
        /// </summary>
        public SavedQueryInfo SaveOrUpdate(IList<SavedQueryInfo> queries, SavedQueryInfo newQuery)
        {
            if (queries == null)
            {
                throw new ArgumentNullException("queries");
            }

            if (newQuery == null)
            {
                throw new ArgumentNullException("newQuery");
            }

            SavedQueryInfo existing = queries.FirstOrDefault(item =>
                SameName(item.ConnectionId, newQuery.ConnectionId) &&
                SameName(item.DatabaseName, newQuery.DatabaseName) &&
                SameName(item.SchemaName, newQuery.SchemaName) &&
                SameName(item.Title, newQuery.Title));

            if (existing == null)
            {
                queries.Add(newQuery);
                Save(queries);
                return newQuery;
            }

            existing.ConnectionName = newQuery.ConnectionName;
            existing.Sql = newQuery.Sql;
            existing.SchemaName = newQuery.SchemaName;
            existing.DatabaseName = newQuery.DatabaseName;
            existing.UpdatedAt = DateTime.Now;
            Save(queries);
            return existing;
        }

        /// <summary>
        /// 删除指定保存查询。
        /// </summary>
        public void DeleteById(IList<SavedQueryInfo> queries, string queryId)
        {
            if (queries == null)
            {
                return;
            }

            RemoveWhere(queries, item => SameName(item.Id, queryId));
            Save(queries);
        }

        /// <summary>
        /// 删除指定连接下的全部保存查询。
        /// </summary>
        public void DeleteByConnection(IList<SavedQueryInfo> queries, string connectionId)
        {
            if (queries == null)
            {
                return;
            }

            RemoveWhere(queries, item => SameName(item.ConnectionId, connectionId));
            Save(queries);
            DeleteConnectionFolder(connectionId);
        }

        /// <summary>
        /// 按条件从 IList 中删除元素，兼容 ObservableCollection 和 List。
        /// </summary>
        private static void RemoveWhere(IList<SavedQueryInfo> queries, Func<SavedQueryInfo, bool> predicate)
        {
            if (queries == null || predicate == null)
            {
                return;
            }

            for (int index = queries.Count - 1; index >= 0; index--)
            {
                if (predicate(queries[index]))
                {
                    queries.RemoveAt(index);
                }
            }
        }

        /// <summary>
        /// 删除指定连接对应的保存查询目录。
        /// </summary>
        public void DeleteConnectionFolder(string connectionId)
        {
            string folder = GetConnectionFolder(connectionId);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        /// <summary>
        /// 获取指定连接和数据库对应的查询 JSON 文件路径。
        /// </summary>
        public string GetQueryFilePath(string connectionId, string databaseName)
        {
            return Path.Combine(GetConnectionFolder(connectionId), AppStoragePaths.ToSafeFileName(NormalizeKey(databaseName)) + ".json");
        }

        /// <summary>
        /// 读取导入文件中的查询语句，支持单条对象或数组两种 JSON 结构。
        /// </summary>
        public IList<SavedQueryInfo> ImportFromFile(string fileName)
        {
            return ReadQueryFile(fileName);
        }

        /// <summary>
        /// 将查询语句导出到指定 JSON 文件。
        /// </summary>
        public void ExportToFile(string fileName, IEnumerable<SavedQueryInfo> queries)
        {
            List<SavedQueryInfo> saveQueries = queries == null ? new List<SavedQueryInfo>() : queries.ToList();
            AppStoragePaths.EnsureFolder(Path.GetDirectoryName(fileName));
            File.WriteAllText(fileName, JsonConvert.SerializeObject(saveQueries, Formatting.Indented), Encoding.UTF8);
        }

        /// <summary>
        /// 清理旧 JSON 文件，避免删除查询后磁盘上残留空文件。
        /// </summary>
        private void ClearExistingJsonFiles()
        {
            if (!Directory.Exists(rootFolder))
            {
                return;
            }

            foreach (string fileName in Directory.GetFiles(rootFolder, "*.json", SearchOption.AllDirectories))
            {
                File.Delete(fileName);
            }
        }

        /// <summary>
        /// 读取单个 JSON 文件中的查询语句。
        /// </summary>
        private static IList<SavedQueryInfo> ReadQueryFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName) || !File.Exists(fileName))
            {
                return new List<SavedQueryInfo>();
            }

            string json = File.ReadAllText(fileName, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<SavedQueryInfo>();
            }

            JToken token = JToken.Parse(json);
            if (token.Type == JTokenType.Array)
            {
                return token.ToObject<List<SavedQueryInfo>>() ?? new List<SavedQueryInfo>();
            }

            SavedQueryInfo query = token.ToObject<SavedQueryInfo>();
            return query == null ? new List<SavedQueryInfo>() : new List<SavedQueryInfo> { query };
        }

        /// <summary>
        /// 读取旧版 XML 保存查询，并迁移到新的 JSON 文件。
        /// </summary>
        private List<SavedQueryInfo> LoadLegacyQueries()
        {
            using (FileStream stream = File.OpenRead(legacyFilePath))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(List<SavedQueryInfo>));
                return serializer.Deserialize(stream) as List<SavedQueryInfo> ?? new List<SavedQueryInfo>();
            }
        }

        /// <summary>
        /// 获取指定连接的保存查询目录。
        /// </summary>
        private string GetConnectionFolder(string connectionId)
        {
            return Path.Combine(rootFolder, AppStoragePaths.ToSafeFileName(NormalizeKey(connectionId)));
        }

        /// <summary>
        /// 规范化用于目录和分组的键。
        /// </summary>
        private static string NormalizeKey(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();
        }

        /// <summary>
        /// 使用忽略大小写方式比较名称。
        /// </summary>
        private static bool SameName(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
