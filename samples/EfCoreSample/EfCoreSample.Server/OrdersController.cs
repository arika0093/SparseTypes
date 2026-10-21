using EfCoreSample.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SparseTypes.EntityFrameworkCore;

namespace EfCoreSample.Server;

[ApiController]
[Route("orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly SampleDbContext _db;
    private readonly ISparseEntityMapper<OrderEntity, OrderDto> _mapper;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(SampleDbContext db, ISparseEntityMapper<OrderEntity, OrderDto> mapper, ILogger<OrdersController> logger)
    {
        _db = db;
        _mapper = mapper;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating a new order with placeholder values");
        var entity = new OrderEntity
        {
            CustomerName = "New customer",
            Notes = null,
            Code = "new",
            Address = new AddressEntity { Street = string.Empty, City = string.Empty },
            Lines =
            [
                new OrderLineEntity
                {
                    Quantity = 1,
                    UnitPrice = 100m,
                    SortOrder = 0,
                },
            ],
        };
        _db.Orders.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created order {OrderId}", entity.Id);
        return Created($"/orders/{entity.Id}", _mapper.ToDto(_db, entity));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetAsync(int id, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Loading order {OrderId}", id);
        var entity = await QueryOrders(id).AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (entity is null)
        {
            _logger.LogWarning("Order {OrderId} was not found", id);
            return NotFound();
        }

        var order = _mapper.ToDto(_db, entity);
        _logger.LogInformation("Loaded order {OrderId} with {LineCount} lines", id, order.Lines.Count);
        return Ok(order);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAsync(
        int id,
        [FromBody] OrderDto.ChangePayload payload,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken
    )
    {
        _logger.LogInformation("Received change payload for order {OrderId}", id);
        var result = await SparseEntitySavePipeline.ExecuteAsync<(SparseEntityApplyResult Applied, OrderDto Baseline), OrderDto>(
            prepare: async ct =>
            {
                var entity = await QueryOrders(id).SingleOrDefaultAsync(ct);
                if (entity is null)
                {
                    _logger.LogWarning("Cannot update order {OrderId} because it was not found", id);
                    throw new RejectedUpdateException(NotFound());
                }

                var current = _mapper.ToDto(_db, entity);
                OrderDto.ChangeSet changes;
                try
                {
                    changes = payload.ToChangeSet();
                }
                catch (ArgumentException exception)
                {
                    _logger.LogWarning(exception, "Rejected invalid change payload for order {OrderId}", id);
                    throw new RejectedUpdateException(BadRequest());
                }

                var accepted = changes.RebaseOnto(current);
                if (accepted.HasConflicts)
                {
                    _logger.LogWarning("Rejected update for order {OrderId} because it has conflicts", id);
                    throw new RejectedUpdateException(
                        Conflict(
                            accepted.Conflicts.Select(static conflict => new
                            {
                                path = conflict.PathText,
                                kind = conflict.Kind.ToString(),
                                reason = conflict.Reason,
                            })
                        )
                    );
                }

                if (!Guid.TryParse(ifMatch?.Trim('"'), out var clientVersion))
                {
                    _logger.LogWarning("Rejected update for order {OrderId} without an If-Match version", id);
                    throw new RejectedUpdateException(
                        Problem(
                            title: "If-Match header with the order version is required.",
                            detail: "Read the order again and send its Version back as If-Match.",
                            statusCode: StatusCodes.Status400BadRequest
                        )
                    );
                }

                _db.Entry(entity).Property(static order => order.Version).OriginalValue = clientVersion;
                return (_mapper.ApplyWithResult(_db, entity, accepted.Rebased), current);
            },
            save: (_, ct) => _db.SaveChangesAsync(ct),
            readBack: async (context, ct) =>
            {
                var persisted = await QueryOrders(id).AsNoTracking().SingleAsync(ct);
                var authoritative = _mapper.ToDto(_db, persisted);
                _mapper.AssignTemporaryKeys(_db, context.Applied, context.Baseline, authoritative);
                return authoritative;
            },
            synchronize: static (_, _, _) => Task.CompletedTask,
            cancellationToken
        );

        if (result.Error is RejectedUpdateException rejected)
        {
            return rejected.Response;
        }

        if (result.Error is OperationCanceledException canceled)
        {
            throw canceled;
        }

        if (result.Stage is SparseEntitySaveStage.ConcurrencyConflict)
        {
            return HandleConcurrencyConflict(id, result.Error);
        }

        if (result.Stage is SparseEntitySaveStage.Saved && result.ReadBack is not null)
        {
            _logger.LogInformation("Updated order {OrderId}; {LineCount} lines persisted", id, result.ReadBack.Lines.Count);
            return Ok(result.ReadBack);
        }

        var detail = result.Stage switch
        {
            SparseEntitySaveStage.PreparationFailed => "The order update was not saved.",
            SparseEntitySaveStage.SaveOutcomeUnknown => "The order update may have been saved. Read the order before submitting another update.",
            SparseEntitySaveStage.ReadBackFailed or SparseEntitySaveStage.ClientSynchronizationFailed =>
                "The order was saved. Read the order before submitting another update.",
            _ => "The order was saved, but its response could not be prepared.",
        };
        _logger.LogError(result.Error, "Order {OrderId} update failed during {SaveStage}", id, result.Stage);
        return Problem(title: "The order update could not be completed.", detail: detail, statusCode: StatusCodes.Status500InternalServerError);
    }

    private IQueryable<OrderEntity> QueryOrders(int id) =>
        _db.Orders.Where(order => order.Id == id).Include(static order => order.Address).Include(static order => order.Lines);

    private IActionResult HandleConcurrencyConflict(int id, Exception? exception)
    {
        _logger.LogWarning(exception, "Rejected stale update for order {OrderId}; the client must refresh", id);
        return Problem(
            title: "The order changed since it was read.",
            detail: "Read the order again, rebase pending edits onto it, and submit with the fresh Version as If-Match.",
            statusCode: StatusCodes.Status412PreconditionFailed
        );
    }

    private sealed class RejectedUpdateException : Exception
    {
        public RejectedUpdateException(IActionResult response)
        {
            Response = response;
        }

        public IActionResult Response { get; }
    }
}
