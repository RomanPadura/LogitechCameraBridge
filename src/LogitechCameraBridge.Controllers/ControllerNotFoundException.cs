namespace LogitechCameraBridge.Controllers;

public sealed class ControllerNotFoundException(string message) : InvalidOperationException(message);
