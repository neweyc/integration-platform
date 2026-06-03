namespace ControlPlane.Infrastructure;

public class ValidationException(string message) : Exception(message);
public class ConflictException(string message) : Exception(message);
public class NotFoundException(string message) : Exception(message);
public class UnauthorizedException(string message) : Exception(message);
