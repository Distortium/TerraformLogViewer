using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using TerraformLogViewer.Models;
using TerraformLogViewer.Services;

namespace TerraformLogViewer
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorPages();
            builder.Services.AddServerSideBlazor();
            //builder.Services.AddHttpContextAccessor();

            //builder.Services.AddDistributedMemoryCache();
            //builder.Services.AddSession(options =>
            //{
            //    options.IdleTimeout = TimeSpan.FromMinutes(30);
            //    options.Cookie.HttpOnly = true;
            //    options.Cookie.IsEssential = true;
            //});

            // Database Configuration
            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

            // Services
            //builder.Services.AddScoped<AuthService>();
            builder.Services.AddScoped<LogParserService>();
            builder.Services.AddScoped<VisualizationService>();
            builder.Services.AddScoped<IntegrationService>();
            builder.Services.AddScoped<IUserService, UserService>();

            // HTTP Client
            builder.Services.AddHttpClient<IntegrationService>();
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("ApiPolicy", policy =>
                {
                    policy.WithOrigins("https://your-frontend-app.com")
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
                });
            });

            var app = builder.Build();

            app.UseCors("ApiPolicy");

            // Ensure database is created with retry logic
            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                var logger = services.GetRequiredService<ILogger<Program>>();

                try
                {
                    // Initialize database first
                    var context = services.GetRequiredService<AppDbContext>();
                    await WaitForDatabaseAsync(context, logger);

                    // ������� ���� ������ � �������
                    //await context.Database.EnsureCreatedAsync();
                    await context.Database.MigrateAsync();
                    logger.LogInformation("Database initialized successfully");

                    // Initialize Elasticsearch with retry
                    /*
                    var elasticSearch = services.GetRequiredService<ElasticSearchService>();
                    var elasticReady = await WaitForElasticsearchAsync(elasticSearch, logger);

                    if (elasticReady)
                    {
                        try
                        {
                            await elasticSearch.CreateIndexAsync();
                            logger.LogInformation("Elasticsearch initialized successfully");
                        }
                        catch (Exception ex)
                        {
                            // �������� ������, �� �� ��������� ����������
                            logger.LogWarning("Failed to create Elasticsearch index, but continuing: {Error}", ex.Message);
                        }
                    }
                    else
                    {
                        logger.LogWarning("Elasticsearch is not available, but continuing without it");
                    }*/
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "An error occurred during initialization");
                    // �� ������� ���������� - ��������� ���������� �����������
                }
            }

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            //app.UseSession();
            app.MapBlazorHub();
            app.MapFallbackToPage("/_Host");

            // ��������� ����������
            try
            {
                await app.RunAsync();
            }
            catch (Exception ex)
            {
                var logger = app.Services.GetRequiredService<ILogger<Program>>();
                logger.LogCritical(ex, "Application terminated unexpectedly");
                throw;
            }
        }

        private static async Task WaitForDatabaseAsync(AppDbContext context, ILogger<Program> logger, int maxRetries = 10)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    if (await context.Database.CanConnectAsync())
                    {
                        logger.LogInformation("Database connection established");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Database not ready yet. Retrying in 5 seconds... (Attempt {Attempt}/{MaxRetries})", i + 1, maxRetries);
                    await Task.Delay(5000);
                }
            }
            throw new Exception("Could not connect to database after multiple attempts");
        }
    }
}