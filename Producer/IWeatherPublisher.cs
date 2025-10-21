namespace Producer
{
  public interface IWeatherPublisher
  {
    Task PublishForecastAsync(
      WeatherForecast[] weatherForecast,
      CancellationToken cancellationToken = default
    );
  }
}
