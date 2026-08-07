using Urbeat.Application.Interfaces;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Urbeat.WebApi.Infrastructure;

public sealed class ShortGuidModelBinder : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueProviderResult == ValueProviderResult.None)
            return;

        var raw = valueProviderResult.FirstValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, "The value cannot be empty.");
            return;
        }

        // Try short code lookup first (8-char codes)
        if (raw.Length == 8)
        {
            var svc = bindingContext.HttpContext.RequestServices.GetRequiredService<IShortIdService>();
            var entityId = await svc.DecodeAsync(raw);
            if (entityId.HasValue)
            {
                bindingContext.Result = ModelBindingResult.Success(entityId.Value);
                return;
            }
        }

        // Fallback: try parsing as a regular GUID (backward compat)
        if (Guid.TryParse(raw, out var guid))
        {
            bindingContext.Result = ModelBindingResult.Success(guid);
            return;
        }

        bindingContext.ModelState.TryAddModelError(bindingContext.ModelName, $"Invalid ID format: '{raw}'.");
    }
}

public sealed class ShortGuidModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        return context.Metadata.ModelType == typeof(Guid)
            ? new ShortGuidModelBinder()
            : null;
    }
}
