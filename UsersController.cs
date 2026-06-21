using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    public UsersController(AppDbContext db) => _db = db;

    // Barcha xodimlar.
    //  • Oddiy holatda (login uchun) faqat FAOL xodimlar qaytadi.
    //  • includeInactive=true bo'lsa — admin paneli uchun NOFAOL xodimlar ham
    //    qaytadi (nofaol qilingan xodim ro'yxatdan yo'qolib qolmasligi uchun).
    // IsActive ham qaytadi — desktop "Faol / Faol emas" holatini to'g'ri ko'rsatadi.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? role, [FromQuery] bool includeInactive = false)
    {
        var query = _db.Users.AsQueryable();
        if (!includeInactive)
            query = query.Where(u => u.IsActive);
        if (!string.IsNullOrEmpty(role))
            query = query.Where(u => u.Role == role);
        var users = await query
            .OrderByDescending(u => u.IsActive)   // faol xodimlar yuqorida
            .ThenBy(u => u.FullName)
            .Select(u => new { u.Id, u.FullName, u.Username, u.Role, u.IsActive })
            .ToListAsync();
        return Ok(users);
    }

    // Faqat sotuvchilar (Android ilova uchun)
    [HttpGet("sellers")]
    public async Task<IActionResult> GetSellers()
    {
        var sellers = await _db.Users
            .Where(u => u.IsActive && u.Role == "Seller")
            .Select(u => new { u.Id, u.FullName, u.Username })
            .ToListAsync();
        return Ok(sellers);
    }

    // Login
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.Username == req.Username && u.Password == req.Password && u.IsActive);

        if (user == null) return Unauthorized(new { message = "Login yoki parol noto'g'ri" });

        return Ok(new { user.Id, user.FullName, user.Username, user.Role });
    }

    // Yangi xodim qo'shish
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] User user)
    {
        // Username takrorlanmasligini tekshirish
        if (await _db.Users.AnyAsync(u => u.Username == user.Username))
            return BadRequest(new { message = "Bu username allaqachon mavjud" });

        user.CreatedAt = DateTime.UtcNow;
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return Ok(new { user.Id, user.FullName, user.Username, user.Role });
    }

    // Xodimni tahrirlash
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] User updated)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();

        user.FullName = updated.FullName;
        user.Username = updated.Username;
        if (!string.IsNullOrEmpty(updated.Password))
            user.Password = updated.Password;
        user.Role = updated.Role;
        user.IsActive = updated.IsActive;

        await _db.SaveChangesAsync();
        return Ok(new { user.Id, user.FullName, user.Username, user.Role });
    }

    // Xodimni o'chirish (deactivate)
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound();
        if (user.Role == "Admin") return BadRequest(new { message = "Admin o'chirilmaydi" });
        user.IsActive = false;
        await _db.SaveChangesAsync();
        return Ok();
    }
}

public class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
