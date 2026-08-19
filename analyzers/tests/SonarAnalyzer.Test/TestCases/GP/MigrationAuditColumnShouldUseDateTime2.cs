namespace FluentMigrator.Builders
{
    public interface IColumnSyntax
    {
        IColumnSyntax WithColumn(string name);
        IColumnSyntax AsDateTime();
        IColumnSyntax AsDateTime2();
    }
}

public class AuditMigration
{
    private const string UpdatedAt = "RowUpdatedAtUtc";

    public void Up(FluentMigrator.Builders.IColumnSyntax table, string dynamicColumn)
    {
        table.WithColumn("RowCreatedAtUtc").AsDateTime(); // Noncompliant {{Use AsDateTime2() for audit column 'RowCreatedAtUtc'.}}
        table.WithColumn(UpdatedAt).AsDateTime(); // Noncompliant {{Use AsDateTime2() for audit column 'RowUpdatedAtUtc'.}}

        table.WithColumn("OccurredAtUtc").AsDateTime();
        table.WithColumn("RowCreatedAtUtc").AsDateTime2();
        table.WithColumn(dynamicColumn).AsDateTime();

        new OwnColumnSyntax().WithColumn("RowCreatedAtUtc").AsDateTime();
    }
}

namespace Own
{
    public class OwnColumnSyntax
    {
        public OwnColumnSyntax WithColumn(string name) => this;
        public OwnColumnSyntax AsDateTime() => this;
    }
}

public sealed class OwnColumnSyntax : Own.OwnColumnSyntax { }
