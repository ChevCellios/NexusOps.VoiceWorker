var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
var connectionString = builder.Configuration.GetConnectionString("NexusOps");
var tenantId = builder.Configuration["NexusOps:TenantId"];
if (!string.IsNullOrWhiteSpace(connectionString) && Guid.TryParse(tenantId, out var parsedTenantId))
{
    builder.Services.AddSingleton(Npgsql.NpgsqlDataSource.Create(connectionString));
    builder.Services.AddSingleton<NexusOps.Web.Services.IOperationsStore>(services =>
        new NexusOps.Web.Services.PostgresOperationsStore(services.GetRequiredService<Npgsql.NpgsqlDataSource>(), parsedTenantId));
}
else
{
    builder.Services.AddSingleton<NexusOps.Web.Services.IOperationsStore, NexusOps.Web.Services.InMemoryOperationsStore>();
}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
