using Microsoft.EntityFrameworkCore;

namespace ResumeApp.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
