using System.Threading.Tasks;

namespace Nexa.Client.Services.Device;

public interface IDeviceRegistrationService
{
    Task<bool> IsDeviceRegisteredAsync();
    Task EnsureDeviceRegisteredAsync(string userId);
    byte[] DecryptData(byte[] encryptedData);
}
