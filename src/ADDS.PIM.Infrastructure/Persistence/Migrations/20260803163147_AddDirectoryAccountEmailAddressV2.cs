using ADDS.PIM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ADDS.PIM.Infrastructure.Persistence.Migrations;

[DbContext(typeof(PimDbContext))]
[Migration("20260803163147_AddDirectoryAccountEmailAddressV2")]
public partial class AddDirectoryAccountEmailAddressV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(
            name: "EmailAddress",
            table: "DirectoryAccounts",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "EmailAddress", table: "DirectoryAccounts");
}
