using Microsoft.AspNetCore.Mvc.RazorPages;
using NexusOps.Web.Models;
using NexusOps.Web.Services;
namespace NexusOps.Web.Pages.Reports;
public sealed class IndexModel(IOperationsStore store) : PageModel { public DashboardSummary Summary { get; private set; }=new(0,0,0,0); public void OnGet()=>Summary=store.GetSummary(); }
