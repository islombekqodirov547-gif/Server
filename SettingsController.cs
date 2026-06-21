using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StoreSystem.Api.Data;
using StoreSystem.Api.Models;

namespace StoreSystem.Api.Controllers;

// Umumiy sozlamalar (kalit/qiymat). Hozircha "DeletePassword" — buxgalter
// o'chirish paroli uchun ishlatiladi. Admin paneli qiymatni o'qiydi/yozadi.
[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    public SettingsController(AppDbContext db) => _db = db;

    // Sozlamani o'qish. Topilmasa — bo'sh qiymat qaytadi (xato emas).
    [HttpGet("{key}")]
    public async Task<IActionResult> Get(string key)
    {
        key = (key ?? "").Trim();
        var s = await _db.Settings.FirstOrDefaultAsync(x => x.Key == key);
        return Ok(new { key, value = s?.Value ?? "" });
    }

    // Sozlamani o'rnatish/yangilash (upsert). Bo'sh qiymat ham qabul qilinadi
    // (masalan o'chirish parolini "talab qilinmasin" qilib qo'yish uchun).
    [HttpPut("{key}")]
    public async Task<IActionResult> Set(string key, [FromBody] SettingRequest req)
    {
        key = (key ?? "").Trim();
        if (string.IsNullOrEmpty(key))
            return BadRequest(new { message = "Kalit bo'sh bo'lishi mumkin emas." });

        var value = (req?.Value ?? "").Trim();
        var s = await _db.Settings.FirstOrDefaultAsync(x => x.Key == key);
        if (s == null)
        {
            s = new Setting { Key = key, Value = value };
            _db.Settings.Add(s);
        }
        else
        {
            s.Value = value;
        }
        await _db.SaveChangesAsync();
        return Ok(new { key, value = s.Value });
    }
}

public class SettingRequest
{
    public string? Value { get; set; }
}
