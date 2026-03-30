using Microsoft.AspNetCore.Mvc;
using AutoRunDailyReport.Repositories;

namespace AutoRunDailyReport.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MesMachinesController : ControllerBase
    {
        private readonly MesRepository _repository;

        public MesMachinesController(MesRepository repository)
        {
            _repository = repository;
        }

        [HttpGet]
        public async Task<IActionResult> GetLatest()
        {
            var data = await _repository.GetLatestMesMachinesAsync();
            return Ok(data);
        }
    }
}