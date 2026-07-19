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
    // 1. DOMAIN MODELS
    // ==========================================
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
    }

    // ==========================================
    // 2. DATABASE MOCKS
    // ==========================================
    public class WriteDatabase { public List<Product> Products = new List<Product>(); }
    public class ReadDatabase { public List<ProductView> ProductViews = new List<ProductView>(); }

    public class ProductView 
    {
        public int Id { get; set; }
        public string DisplayName { get; set; }
        public string PriceText { get; set; } 
    }

    // ==========================================
    // 3. EVENTS & COMMANDS & QUERIES
    // ==========================================
    public record ProductUpdatedEvent(int Id, string Name, decimal Price) : INotification;
    public record UpdateProductPriceCommand(int Id, decimal NewPrice) : IRequest<bool>;
    public record GetProductQuery(int Id) : IRequest<ProductView>;

    // ==========================================
    // 4. HANDLERS
    // ==========================================
    public class UpdateProductPriceHandler : IRequestHandler<UpdateProductPriceCommand, bool>
    {
        private readonly WriteDatabase _writeDb;
        private readonly IMediator _mediator;
        public UpdateProductPriceHandler(WriteDatabase writeDb, IMediator mediator) { _writeDb = writeDb; _mediator = mediator; }

        public async Task<bool> Handle(UpdateProductPriceCommand request, CancellationToken cancellationToken)
        {
            var product = _writeDb.Products.FirstOrDefault(p => p.Id == request.Id);
            if (product == null) return false;
            product.Price = request.NewPrice;
            await _mediator.Publish(new ProductUpdatedEvent(product.Id, product.Name, product.Price));
            return true;
        }
    }

    public class ProductProjectionHandler : INotificationHandler<ProductUpdatedEvent>
    {
        private readonly ReadDatabase _readDb;
        public ProductProjectionHandler(ReadDatabase readDb) => _readDb = readDb;

        public Task Handle(ProductUpdatedEvent notification, CancellationToken cancellationToken)
        {
            var view = _readDb.ProductViews.FirstOrDefault(v => v.Id == notification.Id);
            if (view == null)
                _readDb.ProductViews.Add(new ProductView { Id = notification.Id, DisplayName = notification.Name, PriceText = $"{notification.Price:N2} USD" });
            else
                view.PriceText = $"{notification.Price:N2} USD";
            return Task.CompletedTask;
        }
    }

    public class GetProductQueryHandler : IRequestHandler<GetProductQuery, ProductView>
    {
        private readonly ReadDatabase _readDb;
        public GetProductQueryHandler(ReadDatabase readDb) => _readDb = readDb;
        public Task<ProductView> Handle(GetProductQuery request, CancellationToken cancellationToken) => 
            Task.FromResult(_readDb.ProductViews.FirstOrDefault(p => p.Id == request.Id));
    }

    // ==========================================
    // 5. MAIN PROGRAM (With Table Output)
    // ==========================================
    class Program
    {
        static async Task Main(string[] args)
        {
            var services = new ServiceCollection();
            services.AddLogging(configure =>
            {
                configure.AddConsole(); 
            });
            services.AddSingleton<WriteDatabase>();
            services.AddSingleton<ReadDatabase>();
            services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
            var serviceProvider = services.BuildServiceProvider();
            var mediator = serviceProvider.GetRequiredService<IMediator>();
            var writeDb = serviceProvider.GetRequiredService<WriteDatabase>();

            // Seed
            writeDb.Products.Add(new Product { Id = 1, Name = "Gaming Laptop", Price = 1500 });
            writeDb.Products.Add(new Product { Id = 2, Name = "Mechanical Keyboard", Price = 120 });
            foreach(var p in writeDb.Products.ToList()) 
                await mediator.Publish(new ProductUpdatedEvent(p.Id, p.Name, p.Price));

            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("==============================================================");
            Console.WriteLine("      CQRS SYSTEM - PRODUCT MANAGEMENT DASHBOARD             ");
            Console.WriteLine("==============================================================");
            Console.ResetColor();

            while (true)
            {
                // Display Current State in a Table
                PrintProductTable(serviceProvider.GetRequiredService<ReadDatabase>().ProductViews);

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n[Action] Enter Product ID to update (or 0 to exit): ");
                Console.ResetColor();
                
                if (!int.TryParse(Console.ReadLine(), out int id) || id == 0) break;

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("[Action] Enter New Price: ");
                Console.ResetColor();
                
                if (!decimal.TryParse(Console.ReadLine(), out decimal newPrice))
                {
                    Console.WriteLine("!!! Invalid Price Format !!!");
                    continue;
                }

                // Execute Command
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("\n[System] Executing Command: UpdateProductPrice...");
                bool success = await mediator.Send(new UpdateProductPriceCommand(id, newPrice));
                Console.ResetColor();

                if (success)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("[System] SUCCESS: Command processed and Event published.");
                    Console.ResetColor();
                    // Small delay to simulate async processing/eventual consistency feel
                    await Task.Delay(500); 
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[System] ERROR: Product ID not found!");
                    Console.ResetColor();
                }
            }
        }

        // Helper method to print a professional table
        static void PrintProductTable(List<ProductView> products)
        {
            string header = string.Format("| {0,-5} | {1,-20} | {2,-15} |", "ID", "Product Name", "Price (Read Model)");
            string divider = new string('-', header.Length);

            Console.WriteLine(divider);
            Console.WriteLine(header);
            Console.WriteLine(divider);

            foreach (var p in products)
            {
                Console.WriteLine($"| {p.Id,-5} | {p.DisplayName,-20} | {p.PriceText,-15} |");
            }
            Console.WriteLine(divider);
        }
    }
}
