namespace dr_mcp_dbschema.Tests;

public class DbSchemaHelpersTests
{
    // -------------------------------------------------------------------------
    // ExtractObjectName
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("CREATE TABLE Orders (Id INT)", "Orders")]
    [InlineData("create table orders (id int)", "orders")]
    [InlineData("CREATE TABLE [Orders] (Id INT)", "Orders")]
    public void ExtractObjectName_CreateTable_ReturnsTableName(string sql, string expected)
    {
        var result = DbSchemaHelpers.ExtractObjectName(sql);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("CREATE TABLE dbo.Orders (Id INT)", "dbo.Orders")]
    [InlineData("CREATE TABLE [dbo].[Orders] (Id INT)", "dbo.Orders")]
    public void ExtractObjectName_WithSchema_ReturnsQualifiedName(string sql, string expected)
    {
        var result = DbSchemaHelpers.ExtractObjectName(sql);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("SELECT * FROM Orders")]
    [InlineData("INSERT INTO Orders VALUES (1)")]
    [InlineData("")]
    public void ExtractObjectName_NoTableKeyword_ReturnsNull(string sql)
    {
        var result = DbSchemaHelpers.ExtractObjectName(sql);
        Assert.Null(result);
    }

    // -------------------------------------------------------------------------
    // AnalyzeAlterRisk
    // -------------------------------------------------------------------------

    [Fact]
    public void AnalyzeAlterRisk_AddColumn_ReturnsWarn()
    {
        var (level, warnings) = DbSchemaHelpers.AnalyzeAlterRisk(
            "ALTER TABLE Orders ADD Discount DECIMAL(10,2)");

        Assert.Equal("WARN", level);
        Assert.Contains("ADD", warnings);
    }

    [Fact]
    public void AnalyzeAlterRisk_DropColumn_ReturnsDanger()
    {
        var (level, warnings) = DbSchemaHelpers.AnalyzeAlterRisk(
            "ALTER TABLE Orders DROP COLUMN Discount");

        Assert.Equal("DANGER", level);
        Assert.Contains("DROP COLUMN", warnings);
    }

    [Fact]
    public void AnalyzeAlterRisk_AlterColumn_ReturnsDanger()
    {
        var (level, warnings) = DbSchemaHelpers.AnalyzeAlterRisk(
            "ALTER TABLE Orders ALTER COLUMN Price DECIMAL(20,4)");

        Assert.Equal("DANGER", level);
        Assert.Contains("ALTER COLUMN", warnings);
    }

    [Fact]
    public void AnalyzeAlterRisk_DropConstraint_ReturnsDanger()
    {
        var (level, _) = DbSchemaHelpers.AnalyzeAlterRisk(
            "ALTER TABLE Orders DROP CONSTRAINT FK_Orders_Customers");

        Assert.Equal("DANGER", level);
    }

    [Fact]
    public void AnalyzeAlterRisk_MultipleOps_DropColumnAndAdd_ReturnsDanger()
    {
        var (level, warnings) = DbSchemaHelpers.AnalyzeAlterRisk(
            "ALTER TABLE Orders DROP COLUMN OldField; ALTER TABLE Orders ADD NewField INT");

        Assert.Equal("DANGER", level);
        Assert.Contains("DROP COLUMN", warnings);
    }

    [Fact]
    public void AnalyzeAlterRisk_NoDestructiveOp_ReturnsNoDistruttiveRilevate()
    {
        var (_, warnings) = DbSchemaHelpers.AnalyzeAlterRisk(
            "ALTER TABLE Orders ADD CONSTRAINT uq_code UNIQUE (Code)");

        // ADD senza altro distruttivo → WARN con messaggio operazione additiva
        Assert.Contains("additiva", warnings);
    }

    // -------------------------------------------------------------------------
    // MaskConnectionString
    // -------------------------------------------------------------------------

    [Fact]
    public void MaskConnectionString_WithPassword_MasksIt()
    {
        var cs = "Server=localhost;Database=Test;Password=secret123;";
        var result = DbSchemaHelpers.MaskConnectionString(cs);

        Assert.Contains("Password=***", result);
        Assert.DoesNotContain("secret123", result);
    }

    [Fact]
    public void MaskConnectionString_WithPwd_MasksIt()
    {
        var cs = "Server=localhost;Database=Test;Pwd=mypassword;";
        var result = DbSchemaHelpers.MaskConnectionString(cs);

        Assert.Contains("Pwd=***", result);
        Assert.DoesNotContain("mypassword", result);
    }

    [Fact]
    public void MaskConnectionString_WithPasswordCaseInsensitive_MasksIt()
    {
        var cs = "Server=localhost;DATABASE=Test;PASSWORD=SeCrEt;";
        var result = DbSchemaHelpers.MaskConnectionString(cs);

        Assert.DoesNotContain("SeCrEt", result);
    }

    [Fact]
    public void MaskConnectionString_NoPassword_ReturnsUnchanged()
    {
        var cs = "Server=localhost;Database=Test;Trusted_Connection=True;";
        var result = DbSchemaHelpers.MaskConnectionString(cs);

        Assert.Equal(cs, result);
    }

    [Fact]
    public void MaskConnectionString_EmptyPassword_MasksKey()
    {
        var cs = "Server=localhost;Password=;Database=Test;";
        var result = DbSchemaHelpers.MaskConnectionString(cs);

        Assert.Contains("Password=***", result);
    }
}
