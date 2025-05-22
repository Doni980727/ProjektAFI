using Microsoft.AspNetCore.Mvc;

namespace ProjektAFI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WordsController : ControllerBase
    {
        private static readonly string[] Words = { "katt", "bil", "hus", "träd", "dator", "boll" };
        private readonly Random _random = new();

        [HttpGet("random")]
        public IActionResult GetRandomWord()
        {
            var word = Words[_random.Next(Words.Length)];
            return Ok(word);
        }
    }
}
