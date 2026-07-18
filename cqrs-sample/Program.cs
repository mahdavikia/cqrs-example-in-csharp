using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CQRSFullExample
{
    // ==========================================
    // DOMAIN MODELS (The Core)
    // ==========================================
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
    }

    // ==========================================
    // DATABASE MOCKS (Simulating Write & Read DBs)
    // ==========================================
    public class WriteDatabase
    {
        public List<Product> Products = new List<Product>();
    }

    public class ReadDatabase
    {
        public List<ProductView> ProductViews = new List<ProductView>();
    }

    public class ProductView // Denormalized Model
    {
        public int Id { get; set; }
        public string DisplayName { get; set; }
        public string PriceText { get; set; } 
    }

    // ==========================================
    // EVENTS (The Bridge)
    // ==========================================
    public record ProductUpdatedEvent(int Id, string Name, decimal Price) : INotification;

    // ==========================================
    // COMMAND SIDE (Write Side)
    // ==========================================
    public record UpdateProductPriceCommand(int Id, decimal NewPrice) : IRequest<bool>;

    public class UpdateProductPriceHandler : IRequestHandler<UpdateProductPriceCommand, bool>
    {
        private readonly WriteDatabase _writeDb;
        private readonly IMediator _mediator;

        public UpdateProductPriceHandler(WriteDatabase writeDb, IMediator mediator)
        {
            _writeDb = writeDb;
            _mediator = mediator;
        }

        public async Task<bool> Handle(UpdateProductPriceCommand request, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[Command] Updating Price for Product {request.Id} to {request.NewPrice}...");
            
            var product = _writeDb.Products.FirstOrDefault(p => p.Id == request.Id);
            if (product == null) return false;
            product.Price = request.NewPrice;
            await _mediator.Publish(new ProductUpdatedEvent(product.Id, product.Name, product.Price));

            return true;
        }
    }

    // ==========================================
    // PROJECTION (The Synchronizer)
    // ==========================================
    public class ProductProjectionHandler : INotificationHandler<ProductUpdatedEvent>
    {
        private readonly ReadDatabase _readDb;

        public ProductProjectionHandler(ReadDatabase readDb)
        {
            _readDb = readDb;
        }

        public Task Handle(ProductUpdatedEvent notification, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[Projection] Syncing Read Model for Product {notification.Id}...");

            var view = _readDb.ProductViews.FirstOrDefault(v => v.Id == notification.Id);
            if (view == null)
            {
                _readDb.ProductViews.Add(new ProductView
                {
                    Id = notification.Id,
                    DisplayName = notification.Name,
                    PriceText = $"{notification.Price:C}" // Denormalized field
                });
            }
            else
            {
                view.PriceText = $"{notification.Price:C}";
            }

            return Task.CompletedTask;
        }
    }

    // ==========================================
    // QUERY SIDE (Read Side)
    // ==========================================
    public record GetProductQuery(int Id) : IRequest<ProductView>;

    public class GetProductQueryHandler : IRequestHandler<GetProductQuery, ProductView>
    {
        private readonly ReadDatabase _readDb;

        public GetProductQueryHandler(ReadDatabase readDb)
        {
            _readDb = readDb;
        }

        public Task<ProductView> Handle(GetProductQuery request, CancellationToken cancellationToken)
        {
            Console.WriteLine($"[Query] Fetching Product {request.Id} from Read DB...");
            var result = _readDb.ProductViews.FirstOrDefault(p => p.Id == request.Id);
            return Task.FromResult(result);
        }
    }

    // ==========================================
    // MAIN PROGRAM (Entry Point)
    // ==========================================
    class Program
    {
        static async Task Main(string[] args)
        {
            var services = new ServiceCollection();

            services.AddLogging(configure => configure.AddConsole());

            services.AddSingleton<WriteDatabase>();
            services.AddSingleton<ReadDatabase>();

            services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

            var serviceProvider = services.BuildServiceProvider();

            var writeDb = serviceProvider.GetRequiredService<WriteDatabase>();
            var readDb = serviceProvider.GetRequiredService<ReadDatabase>();
            var mediator = serviceProvider.GetRequiredService<IMediator>();

            writeDb.Products.Add(new Product { Id = 1, Name = "Laptop", Price = 1000 });
            
            await mediator.Publish(new ProductUpdatedEvent(1, "Laptop", 1000));

            Console.WriteLine("--- Initial State ---");
            var initialView = await mediator.Send(new GetProductQuery(1));
            Console.WriteLine($"Product: {initialView?.DisplayName}, Price: {initialView?.PriceText}");
            Console.WriteLine("---------------------\n");


            var command = new UpdateProductPriceCommand(1, 1200);
            await mediator.Send(command);

            Console.WriteLine("\n--- After Command Execution ---");

            var updatedView = await mediator.Send(new GetProductQuery(1));
            Console.WriteLine($"Product: {updatedView?.DisplayName}, Price: {updatedView?.PriceText}");
            Console.WriteLine("-------------------------------");

            Console.WriteLine("\nPress any key to exit...");
            //Console.ReadKey();
            while (true)
            {
                try
                {
                    Console.Write("\nEnter Product ID to update (or 0 to exit): ");
                    int id = int.Parse(Console.ReadLine());
                    if (id == 0) break;

                    Console.Write("Enter New Price: ");
                    decimal newPrice = decimal.Parse(Console.ReadLine());
                    Console.WriteLine("\n[System] Sending Command...");
                    bool success = await mediator.Send(new UpdateProductPriceCommand(id, newPrice));

                    if (success)
                    {
                        Console.WriteLine("[System] Command Success!");
                        
                        Console.WriteLine("[System] Fetching updated data via Query...");
                        var _updatedView = await mediator.Send(new GetProductQuery(id));
                        
                        if (updatedView != null)
                        {
                            Console.WriteLine("\n>>> RESULT FROM READ MODEL <<<");
                            Console.WriteLine($"Product: {_updatedView.DisplayName}");
                            Console.WriteLine($"New Price: {_updatedView.PriceText}");
                            Console.WriteLine(">>>>>>>>>>>>>>>>>>>>>>>>>>>>>>\n");
                        }
                    }
                    else
                    {
                        Console.WriteLine("[Error] Product not found!");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Error] Invalid input: {ex.Message}");
                }
            }
        }
    }
}
