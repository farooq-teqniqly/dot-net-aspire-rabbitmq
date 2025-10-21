namespace Producer
{
  public interface IWeatherPublisher
  {
    Task PublishForecastAsync(
      WeatherForecast[] weatherForecast,
      Guid messageId,
      CancellationToken cancellationToken = default
    );
  }
}
