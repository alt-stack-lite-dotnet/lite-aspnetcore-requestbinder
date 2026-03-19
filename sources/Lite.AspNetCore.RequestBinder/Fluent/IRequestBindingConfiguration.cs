namespace Lite.AspNetCore.RequestBinder.Fluent;

public interface IRequestBindingConfiguration<TRequest>
{
    void Configure(IRequestBindingBuilder<TRequest> builder);
}
