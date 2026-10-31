using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApexPost.Database.Migrations
{
    /// <summary>
    /// Corrects seed data written by InitialCreate. Staff.IsActive has a database default of 1
    /// (DF_Staff_IsActive). For a seeded row whose value equals the CLR default (false), EF Core's
    /// HasData leaves the column out of the INSERT, so SQL Server applied the default and the
    /// inactive demo clerk (Id 24, former.clerk@apexpost.local) became active. Found by the SQL
    /// Server integration run of InactiveStaff_CannotOpenTransactions; the in-memory fakes could
    /// not show it. The model already says IsActive = false, so only the data needs fixing.
    /// </summary>
    public partial class FixInactiveStaffSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Staff",
                keyColumn: "Id",
                keyValue: 24,
                column: "IsActive",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Staff",
                keyColumn: "Id",
                keyValue: 24,
                column: "IsActive",
                value: true);
        }
    }
}
