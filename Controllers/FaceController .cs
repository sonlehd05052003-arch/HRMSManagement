using HRManagement.DTOs.Attendances;
using HRManagement.Services.CurrentUsers;
using HRManagement.Services.FaceVerifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRManagement.Controllers
{
    [ApiController]
    [Route("api/face")]
    [Authorize]
    public class FaceController : ControllerBase
    {
        private readonly IFaceVerificationService _faceVerificationService;
        private readonly ICurrentUserService _currentUserService;

        public FaceController(
            IFaceVerificationService faceVerificationService,
            ICurrentUserService currentUserService)
        {
            _faceVerificationService = faceVerificationService;
            _currentUserService = currentUserService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> RegisterFace([FromBody] FaceRegisterRequestDto request)
        {
            var employeeId = await _currentUserService.GetCurrentEmployeeIdAsync();

            if (employeeId <= 0)
                return Unauthorized(new { message = "Không xác định được nhân viên hiện tại." });

            if (request == null || string.IsNullOrWhiteSpace(request.ReferenceImageBase64))
                return BadRequest(new { message = "Ảnh khuôn mặt là bắt buộc." });

            var imagePath = await _faceVerificationService.RegisterFaceAsync(
                employeeId,
                request.ReferenceImageBase64
            );

            return Ok(new
            {
                message = "Đăng ký khuôn mặt thành công.",
                referenceImagePath = imagePath
            });
        }

        [HttpGet("check-registration")]
        public async Task<IActionResult> CheckRegistrationStatus()
        {
            var employeeId = await _currentUserService.GetCurrentEmployeeIdAsync();

            if (employeeId <= 0)
                return Unauthorized(new { message = "Không xác định được nhân viên hiện tại." });

            var isRegistered = await _faceVerificationService.IsFaceRegisteredAsync(employeeId);
            return Ok(new
            {
                isRegistered
            });
        }

        // ─── HR/Admin Endpoints ────────────────────────────────────────────────────

        [HttpGet("admin/employees")]
        public async Task<IActionResult> GetAllEmployeesFaceStatus()
        {
            var result = await _faceVerificationService.GetAllEmployeesFaceStatusAsync();
            return Ok(result);
        }

        [HttpPost("admin/register/{employeeId:int}")]
        public async Task<IActionResult> AdminRegisterFace(int employeeId, [FromBody] FaceRegisterRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ReferenceImageBase64))
                return BadRequest(new { message = "Ảnh khuôn mặt là bắt buộc." });

            var imagePath = await _faceVerificationService.RegisterFaceAsync(employeeId, request.ReferenceImageBase64);
            return Ok(new
            {
                message = $"Đăng ký khuôn mặt cho nhân viên #{employeeId} thành công.",
                referenceImagePath = imagePath
            });
        }

        [HttpDelete("admin/{employeeId:int}")]
        public async Task<IActionResult> AdminDeleteFace(int employeeId)
        {
            await _faceVerificationService.DeleteFaceAsync(employeeId);
            return Ok(new { message = $"Đã xóa hồ sơ khuôn mặt của nhân viên #{employeeId}." });
        }

        // ──────────────────────────────────────────────────────────────────────────

        [HttpPost("verify")]
        public async Task<IActionResult> VerifyFace([FromBody] CheckInRequestDto request)
        {
            var employeeId = await _currentUserService.GetCurrentEmployeeIdAsync();

            if (employeeId <= 0)
                return Unauthorized(new { message = "Không xác định được nhân viên hiện tại." });

            if (request == null || string.IsNullOrWhiteSpace(request.FaceImageBase64))
                return BadRequest(new { message = "Ảnh xác thực là bắt buộc." });

            var result = await _faceVerificationService.VerifyAsync(
                employeeId,
                request.FaceImageBase64,
                "CheckIn",
                request.DeviceInfo,
                request.IpAddress,
                request.Location
            );

            if (!result.IsMatch)
            {
                return Unauthorized(new
                {
                    message = "Xác thực khuôn mặt thất bại.",
                    result.ConfidenceScore,
                    result.ThresholdUsed,
                    result.FailureReason
                });
            }

            return Ok(new
            {
                message = "Xác thực khuôn mặt thành công.",
                result.ConfidenceScore,
                result.ThresholdUsed,
                result.CapturedImagePath
            });
        }
    }
}