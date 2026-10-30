using ApexPost.Models.Domain;
using ApexPost.Services.Abstractions;
using ApexPost.Services.Dashboard;

namespace ApexPost.MVC.Models;

public sealed record DashboardViewModel(BranchDashboard Dashboard, IReadOnlyList<BranchDto> Branches, bool CanSwitchBranch, bool CanTransact);
