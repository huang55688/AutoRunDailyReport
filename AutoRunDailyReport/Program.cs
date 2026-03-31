using AutoRunDailyReport.Repositories;
using AutoRunDailyReport.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Repositories（持有 connection string，singleton 即可）
builder.Services.AddSingleton<MesRepository>();
builder.Services.AddSingleton<TargetRepository>();
builder.Services.AddSingleton<MetaRepository>();

// Sync services
builder.Services.AddSingleton<SyncStatusTracker>();
builder.Services.AddSingleton<SyncService>();
builder.Services.AddHostedService<BackgroundSyncService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
