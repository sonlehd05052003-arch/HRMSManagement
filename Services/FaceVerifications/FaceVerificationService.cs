using HRManagement.DTOs.Attendances;
using HRManagement.Models;
using HRManagement.DataAcess.Interfaces;
using HRManagement.Services.FileStorages;
using Task = System.Threading.Tasks.Task;

namespace HRManagement.Services.FaceVerifications
{
    public class FaceVerificationService : IFaceVerificationService
    {
        private readonly IAttendanceRepository _attendanceRepository;
        private readonly IFileStorageService _fileStorageService;
        private readonly FaceEmbeddingService _faceEmbeddingService;
        private readonly IWebHostEnvironment _env;

        private const decimal MATCH_THRESHOLD = 0.80m;

        public FaceVerificationService(
            IAttendanceRepository attendanceRepository,
            IFileStorageService fileStorageService,
            FaceEmbeddingService faceEmbeddingService,
            IWebHostEnvironment env)
        {
            _attendanceRepository = attendanceRepository;
            _fileStorageService = fileStorageService;
            _faceEmbeddingService = faceEmbeddingService;
            _env = env;
        }

        public async Task<string> RegisterFaceAsync(int employeeId, string referenceImageBase64)
        {
            if (string.IsNullOrWhiteSpace(referenceImageBase64))
                throw new ArgumentException("Reference image is required.");

            var imagePath = await _fileStorageService.SaveBase64ImageAsync(
                referenceImageBase64,
                "face-profiles",
                $"emp_{employeeId}"
            );

            var webRootPath = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var absolutePath = Path.Combine(webRootPath, imagePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

            var embedding = _faceEmbeddingService.ExtractEmbedding(absolutePath);
            var embeddingBytes = FloatArrayToBytes(embedding);
            var embeddingString = Convert.ToBase64String(embeddingBytes);

            var existing = await _attendanceRepository.GetActiveFaceProfileByEmployeeIdAsync(employeeId);

            if (existing == null)
            {
                var profile = new FaceProfile
                {
                    EmployeeId = employeeId,
                    ReferenceImagePath = imagePath,
                    FaceEmbedding = embeddingString,
                    Status = "Active",
                    CreatedDate = DateTime.Now,
                    CreatedBy = employeeId
                };

                await _attendanceRepository.AddFaceProfileAsync(profile);
            }
            else
            {
                existing.ReferenceImagePath = imagePath;
                existing.FaceEmbedding = embeddingString;
                existing.Status = "Active";
                existing.ModifiedDate = DateTime.Now;
                existing.ModifiedBy = employeeId;

                await _attendanceRepository.UpdateFaceProfileAsync(existing);
            }

            await _attendanceRepository.SaveChangesAsync();
            return imagePath;
        }

        public async Task<bool> IsFaceRegisteredAsync(int employeeId)
        {
            var existing = await _attendanceRepository.GetActiveFaceProfileByEmployeeIdAsync(employeeId);
            return existing != null && !string.IsNullOrWhiteSpace(existing.FaceEmbedding);
        }

        public async Task<List<EmployeeFaceStatusDto>> GetAllEmployeesFaceStatusAsync()
        {
            var rows = await _attendanceRepository.GetAllEmployeesWithFaceProfileAsync();
            return rows.Select(r => new EmployeeFaceStatusDto
            {
                EmployeeId = r.Employee.EmployeeId,
                EmployeeCode = r.Employee.EmployeeCode,
                FullName = r.Employee.FullName,
                Email = r.Employee.Email,
                DepartmentName = r.Employee.Department?.DepartmentName,
                PositionName = r.Employee.Position?.PositionName,
                IsRegistered = r.FaceProfile != null && !string.IsNullOrWhiteSpace(r.FaceProfile.FaceEmbedding),
                RegisteredAt = r.FaceProfile?.CreatedDate,
                LastUpdatedAt = r.FaceProfile?.ModifiedDate
            }).ToList();
        }

        public async Task DeleteFaceAsync(int employeeId)
        {
            var existing = await _attendanceRepository.GetActiveFaceProfileByEmployeeIdAsync(employeeId);
            if (existing == null)
                throw new KeyNotFoundException("Nhân viên chưa có hồ sơ khuôn mặt.");

            existing.Status = "Inactive";
            existing.ModifiedDate = DateTime.Now;

            await _attendanceRepository.UpdateFaceProfileAsync(existing);
            await _attendanceRepository.SaveChangesAsync();
        }

        public async Task<FaceVerificationResultDto> VerifyAsync(
            int employeeId,
            string faceImageBase64,
            string verificationType,
            string? deviceInfo,
            string? ipAddress,
            string? location)
        {
            if (string.IsNullOrWhiteSpace(faceImageBase64))
                throw new ArgumentException("Face image is required.");

            var faceProfile = await _attendanceRepository.GetActiveFaceProfileByEmployeeIdAsync(employeeId);

            if (faceProfile == null || string.IsNullOrWhiteSpace(faceProfile.FaceEmbedding))
            {
                var noProfileResult = new FaceVerificationResultDto
                {
                    IsMatch = false,
                    ConfidenceScore = 0,
                    ThresholdUsed = MATCH_THRESHOLD,
                    LivenessPassed = null,
                    FailureReason = "NoFaceProfile"
                };

                await SaveVerificationLogAsync(
                    employeeId,
                    verificationType,
                    faceImageBase64,
                    noProfileResult,
                    deviceInfo,
                    ipAddress,
                    location);

                return noProfileResult;
            }

            var capturedImagePath = await _fileStorageService.SaveBase64ImageAsync(
                faceImageBase64,
                "face-captures",
                $"{verificationType.ToLower()}_{employeeId}"
            );

            var webRootPath = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var absoluteCapturedPath = Path.Combine(webRootPath, capturedImagePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

            try
            {
                var capturedEmbedding = _faceEmbeddingService.ExtractEmbedding(absoluteCapturedPath);
                var referenceEmbedding = BytesToFloatArray(Convert.FromBase64String(faceProfile.FaceEmbedding));

                var similarity = _faceEmbeddingService.CosineSimilarity(referenceEmbedding, capturedEmbedding);
                var isMatch = similarity >= (float)MATCH_THRESHOLD;

                var result = new FaceVerificationResultDto
                {
                    IsMatch = isMatch,
                    ConfidenceScore = (decimal)similarity,
                    ThresholdUsed = MATCH_THRESHOLD,
                    LivenessPassed = null,
                    FailureReason = isMatch ? null : $"FaceNotMatched (score={similarity:F4}, threshold={MATCH_THRESHOLD:F4})",
                    CapturedImagePath = capturedImagePath
                };

                await SaveVerificationLogAsync(
                    employeeId,
                    verificationType,
                    null,
                    result,
                    deviceInfo,
                    ipAddress,
                    location,
                    capturedImagePath);

                return result;
            }
            catch (Exception ex)
            {
                var failedResult = new FaceVerificationResultDto
                {
                    IsMatch = false,
                    ConfidenceScore = 0m,
                    ThresholdUsed = MATCH_THRESHOLD,
                    LivenessPassed = null,
                    FailureReason = ex.Message,
                    CapturedImagePath = capturedImagePath
                };

                await SaveVerificationLogAsync(
                    employeeId,
                    verificationType,
                    null,
                    failedResult,
                    deviceInfo,
                    ipAddress,
                    location,
                    capturedImagePath);

                return failedResult;
            }
        }

        private async System.Threading.Tasks.Task SaveVerificationLogAsync(
            int employeeId,
            string verificationType,
            string? faceImageBase64,
            FaceVerificationResultDto result,
            string? deviceInfo,
            string? ipAddress,
            string? location,
            string? capturedImagePath = null)
        {
            if (capturedImagePath == null && !string.IsNullOrWhiteSpace(faceImageBase64))
            {
                capturedImagePath = await _fileStorageService.SaveBase64ImageAsync(
                    faceImageBase64,
                    "face-captures",
                    $"{verificationType.ToLower()}_{employeeId}"
                );
            }

            var log = new FaceVerificationLog
            {
                EmployeeId = employeeId,
                AttendanceLogId = null,
                VerificationType = verificationType,
                CapturedImagePath = capturedImagePath,
                ConfidenceScore = result.ConfidenceScore,
                ThresholdUsed = result.ThresholdUsed,
                IsMatch = result.IsMatch,
                LivenessPassed = result.LivenessPassed,
                FailureReason = result.FailureReason,
                VerifiedAt = DateTime.Now,
                DeviceInfo = deviceInfo,
                IpAddress = ipAddress,
                Location = location,
                CreatedBy = employeeId
            };

            await _attendanceRepository.AddFaceVerificationLogAsync(log);
            await _attendanceRepository.SaveChangesAsync();
        }

        private byte[] FloatArrayToBytes(float[] values)
        {
            var bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private float[] BytesToFloatArray(byte[] bytes)
        {
            var values = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            return values;
        }
    }
}