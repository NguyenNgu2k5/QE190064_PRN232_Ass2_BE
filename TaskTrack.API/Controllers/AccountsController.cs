using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTrack.Service.Dtos;
using TaskTrack.Service.Services;

namespace TaskTrack.API.Controllers;

[ApiController, Route("api/accounts"), Authorize(Roles = "Admin")]
public class AccountsController(AccountService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> GetAll(CancellationToken ct) => Ok(await service.GetAllAsync(ct));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(await service.GetAsync(id, ct));
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, AccountUpdateRequest request, CancellationToken ct) => Ok(await service.UpdateAsync(id, request, int.Parse(User.FindFirstValue("AccountID")!), ct));
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) { await service.DeleteAsync(id, ct); return NoContent(); }
}
