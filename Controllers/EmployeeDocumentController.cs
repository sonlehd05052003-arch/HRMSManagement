using Microsoft.AspNetCore.Authorization;
using HRManagement.DTOs;
using HRManagement.Services.Employees;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class EmployeeDocumentController : ControllerBase
    {
        private readonly IEmployeeDocumentService _documentService;

        public EmployeeDocumentController(IEmployeeDocumentService documentService)
        {
            _documentService = documentService;
        }

        [HttpPost("upload")]
        public async Task<ActionResult<EmployeeDocumentResponseDto>> Upload(
            [FromForm] UploadEmployeeDocumentDto uploadDto,
            IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest(new { message = "File is required." });

                var result = await _documentService.UploadDocumentAsync(uploadDto, file);

                if (result == null)
                    return BadRequest(new { message = "Upload failed." });

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpPut("{documentId}")]
        public async Task<ActionResult<EmployeeDocumentResponseDto>> Update(
            int documentId,
            [FromBody] UpdateEmployeeDocumentDto updateDto)
        {
            try
            {
                var result = await _documentService.UpdateDocumentAsync(documentId, updateDto);

                if (result == null)
                    return NotFound(new { message = $"Document {documentId} not found." });

                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
        }

        [HttpGet("employee/{employeeId}")]
        public async Task<ActionResult> GetDocumentsByEmployeeId(int employeeId)
        {
            var documents = await _documentService.GetEmployeeDocumentsAsync(employeeId);

            if (documents == null || !documents.Any())
                return NotFound(new { message = $"No documents found for employee {employeeId}." });

            return Ok(documents);
        }

        [HttpGet("{documentId}")]
        public async Task<ActionResult> GetDocumentById(int documentId)
        {
            var document = await _documentService.GetDocumentByIdAsync(documentId);

            if (document == null)
                return NotFound(new { message = $"Document {documentId} not found." });

            return Ok(document);
        }

        [HttpGet("{documentId}/download")]
        public async Task<ActionResult> Download(int documentId)
        {
            var fileData = await _documentService.DownloadDocumentAsync(documentId);

            if (fileData == null)
                return NotFound(new { message = $"Document {documentId} not found." });

            return File(fileData.Value.fileContent, fileData.Value.contentType, fileData.Value.fileName);
        }

        [HttpDelete("{documentId}")]
        public async Task<ActionResult> Delete(int documentId)
        {
            var success = await _documentService.DeleteDocumentAsync(documentId);

            if (!success)
                return NotFound(new { message = $"Document {documentId} not found." });

            return Ok(new { message = "Document deleted successfully." });
        }

        [HttpGet("employee/{employeeId}/category/{category}")]
        public async Task<ActionResult> GetDocumentsByCategory(int employeeId, string category)
        {
            var documents = await _documentService.GetDocumentsByCategoryAsync(employeeId, category);

            if (documents == null || !documents.Any())
                return NotFound(new
                {
                    message = $"No documents found for employee {employeeId} in category '{category}'."
                });

            return Ok(documents);
        }
    }
}