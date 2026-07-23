using System;
using System.Collections.Generic;
using System.Linq;
using TXDB.Models;

namespace TXDB.Data
{
    /// <summary>
    /// 统一管理数据库类型到 Provider 实现的映射。
    /// </summary>
    public static class DatabaseProviderRegistry
    {
        private static readonly List<IDatabaseProvider> providers = new List<IDatabaseProvider>
        {
            new SqlServerDatabaseProvider(),
            new SQLiteDatabaseProvider(),
            new OracleDatabaseProvider(),
            new AccessDatabaseProvider(),
            new FactoryDatabaseProvider(DatabaseProviderType.MySQL, "MySQL", "MySql.Data.MySqlClient")
        };

        /// <summary>
        /// 获取全部 Provider。
        /// </summary>
        public static IList<IDatabaseProvider> GetProviders()
        {
            return providers.ToList();
        }

        /// <summary>
        /// 根据数据库类型查找 Provider。
        /// </summary>
        public static IDatabaseProvider GetProvider(DatabaseProviderType providerType)
        {
            IDatabaseProvider provider = providers.FirstOrDefault(item => item.ProviderType == providerType);
            if (provider == null)
            {
                throw new InvalidOperationException("未找到数据库 Provider：" + providerType);
            }

            return provider;
        }

        /// <summary>
        /// 获取数据库类型显示名称。
        /// </summary>
        public static string GetDisplayName(DatabaseProviderType providerType)
        {
            return GetProvider(providerType).DisplayName;
        }
    }
}
