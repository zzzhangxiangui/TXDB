using System;
using TXDB.Services;

namespace TXDB.Models
{
    public sealed class TableColumnDefinition : NotifyObject
    {
        private string name = string.Empty;
        private string typeName = string.Empty;
        private string typeArguments = string.Empty;
        private string defaultSql = string.Empty;
        private string comment = string.Empty;
        private bool primaryKey;
        private bool allowNull = true;
        private bool identity;

        public string OriginalName { get; set; }
        public string Name { get { return name; } set { name = value ?? string.Empty; OnPropertyChanged("Name"); } }
        public string TypeName { get { return typeName; } set { typeName = value ?? string.Empty; OnPropertyChanged("TypeName"); } }
        public string TypeArguments { get { return typeArguments; } set { typeArguments = value ?? string.Empty; OnPropertyChanged("TypeArguments"); } }
        public string DefaultSql { get { return defaultSql; } set { defaultSql = value ?? string.Empty; OnPropertyChanged("DefaultSql"); } }
        public string Comment { get { return comment; } set { comment = value ?? string.Empty; OnPropertyChanged("Comment"); } }
        public bool IsPrimaryKey
        {
            get { return primaryKey; }
            set
            {
                primaryKey = value;
                if (value) AllowNull = false;
                OnPropertyChanged("IsPrimaryKey");
                OnPropertyChanged("CanAllowNull");
            }
        }
        public bool AllowNull
        {
            get { return allowNull; }
            set { allowNull = value && !primaryKey; OnPropertyChanged("AllowNull"); }
        }
        public bool IsIdentity { get { return identity; } set { identity = value; OnPropertyChanged("IsIdentity"); } }
        public bool CanAllowNull { get { return !IsPrimaryKey && CanEdit; } }
        public bool CanChangeIdentity { get { return string.IsNullOrEmpty(OriginalName) && CanEdit; } }
        public bool IsComputed { get; set; }
        public bool IsReadOnlyDefinition { get; set; }
        public bool CanEdit { get { return !IsComputed && !IsReadOnlyDefinition; } }
        // 数据库原有属性，修改字段时保留，不在普通字段编辑器中重建。
        public string Extra { get; set; }
        public string Collation { get; set; }
        public string DefaultConstraintName { get; set; }
        public bool HasCommentProperty { get; set; }

        public TableColumnDefinition Clone()
        {
            return new TableColumnDefinition
            {
                OriginalName = OriginalName, Name = Name, TypeName = TypeName, TypeArguments = TypeArguments,
                AllowNull = AllowNull, IsPrimaryKey = IsPrimaryKey, IsIdentity = IsIdentity, DefaultSql = DefaultSql,
                Comment = Comment, IsComputed = IsComputed, IsReadOnlyDefinition = IsReadOnlyDefinition, Extra = Extra, Collation = Collation,
                DefaultConstraintName = DefaultConstraintName, HasCommentProperty = HasCommentProperty
            };
        }

        public bool SameDefinition(TableColumnDefinition other)
        {
            return other != null && Name == other.Name && TypeName == other.TypeName && TypeArguments == other.TypeArguments &&
                   IsPrimaryKey == other.IsPrimaryKey && AllowNull == other.AllowNull && IsIdentity == other.IsIdentity &&
                   DefaultSql == other.DefaultSql && Comment == other.Comment;
        }
    }
}
