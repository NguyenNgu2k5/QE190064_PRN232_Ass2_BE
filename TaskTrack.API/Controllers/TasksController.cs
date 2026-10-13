using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTrack.Service.Dtos;
using TaskTrack.Service.Interfaces;

namespace TaskTrack.API.Controllers;

[ApiController, Route("api/tasks")]
public class TasksController(ITaskService service) : ApiControllerBase
{
    [HttpGet] public Task<ActionResult<IReadOnlyList<TaskResponse>>> GetAll(CancellationToken ct) => Run(() => service.GetAllAsync(ct));
    [HttpGet("search")] public Task<ActionResult<IReadOnlyList<TaskResponse>>> Search([FromQuery] string? title, [FromQuery] short? status, [FromQuery] short? priority, [FromQuery] int? projectId, [FromQuery] int? tagId, CancellationToken ct) => Run(() => service.SearchAsync(title, status, priority, projectId, tagId, ct));
    [HttpGet("project/{projectId:int}")] public Task<ActionResult<IReadOnlyList<TaskResponse>>> ByProject(int projectId, CancellationToken ct) => Run(() => service.GetByProjectAsync(projectId, ct));
    [HttpGet("{id:int}")] public Task<ActionResult<TaskResponse>> GetById(int id, CancellationToken ct) => Run(() => service.GetByIdAsync(id, ct));
    [Authorize, HttpPost] public Task<ActionResult<TaskResponse>> Create(TaskRequest request, CancellationToken ct) => Run(() => service.CreateAsync(request, int.Parse(User.FindFirst("AccountID")!.Value), ct));
    [Authorize, HttpPut("{id:int}")] public Task<ActionResult<TaskResponse>> Update(int id, TaskRequest request, CancellationToken ct) => Run(() => service.UpdateAsync(id, request, int.Parse(User.FindFirst("AccountID")!.Value), ct));
    [Authorize, HttpDelete("{id:int}")] public Task<IActionResult> Delete(int id, CancellationToken ct) => Run(() => service.DeleteAsync(id, int.Parse(User.FindFirst("AccountID")!.Value), ct));
}
