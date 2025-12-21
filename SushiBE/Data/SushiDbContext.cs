using Microsoft.EntityFrameworkCore;
using SushiBE.Models;
using System;

namespace SushiBE.Data
{
    public class SushiDbContext : DbContext
    {
        public SushiDbContext(DbContextOptions<SushiDbContext> options) : base(options) { }

        // DbSets
        public DbSet<CanOrder> CanOrders { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Admin> Admins { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Cart> Carts { get; set; }
        public DbSet<CartItem> CartItems { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<ProductImage> ProductImages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Discriminator for inheritance
            modelBuilder.Entity<User>()
                .HasDiscriminator<string>("UserType")
                .HasValue<Customer>("Customer")
                .HasValue<Admin>("Admin");

            // Product - ProductImage relationship
            modelBuilder.Entity<Product>()
                .HasMany(p => p.Images)
                .WithOne(pi => pi.Product)
                .HasForeignKey(pi => pi.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            // Unique email
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // Explicit Category-Product relationship
            modelBuilder.Entity<Product>()
                .HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Cart>()
                .HasIndex(c => c.CustomerId)
                .IsUnique();

            modelBuilder.Entity<Cart>()
                .HasOne(c => c.Customer)
                .WithOne(cu => cu.Cart)
                .HasForeignKey<Cart>(c => c.CustomerId);

            modelBuilder.Entity<CartItem>()
                .HasIndex(ci => new { ci.CartId, ci.ProductId })
                .IsUnique();

            modelBuilder.Entity<CartItem>()
                .HasOne(ci => ci.Cart)
                .WithMany(c => c.Items)
                .HasForeignKey(ci => ci.CartId);

            modelBuilder.Entity<CartItem>()
                .HasOne(ci => ci.Product)
                .WithMany()
                .HasForeignKey(ci => ci.ProductId);

            modelBuilder.Entity<Cart>()
                .Property(c => c.TotalAmount)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<CartItem>()
                .Property(ci => ci.Price)
                .HasColumnType("decimal(18,2)");

            // Order and OrderItem relationships
            modelBuilder.Entity<Order>()
                .HasOne(o => o.Customer)
                .WithMany()
                .HasForeignKey(o => o.CustomerId);

            modelBuilder.Entity<Order>()
                .Property(o => o.TotalAmount)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.Order)
                .WithMany(o => o.Items)
                .HasForeignKey(oi => oi.OrderId);

            modelBuilder.Entity<OrderItem>()
                .HasOne(oi => oi.Product)
                .WithMany()
                .HasForeignKey(oi => oi.ProductId);

            modelBuilder.Entity<OrderItem>()
                .Property(oi => oi.Price)
                .HasColumnType("decimal(18,2)");

            // CanOrder configuration: standalone table, no relationships
            modelBuilder.Entity<CanOrder>(entity =>
            {
                entity.HasKey(c => c.CanOrderId);
                entity.Property(c => c.IsEnabled).IsRequired();

                // خلّي EF/Npgsql يختار النوع الصحيح
                entity.Property(c => c.OnDate);
                entity.Property(c => c.OffDate);
            });

        }
    }
}
