SET XACT_ABORT ON;
IF EXISTS (SELECT 1 FROM dbo.[User] WITH (UPDLOCK,HOLDLOCK)
    WHERE email IN (N'buyer.a06@example.invalid',N'seller.a06@example.invalid',N'admin.a06@example.invalid'))
    THROW 51001, 'Reserved demo email already exists; no user adopted or overwritten.', 1;

INSERT dbo.[User](username,email,password,role) VALUES(N'Demo Buyer',N'buyer.a06@example.invalid',NULL,N'Buyer');
DECLARE @buyer int=SCOPE_IDENTITY();
INSERT dbo.[User](username,email,password,role) VALUES(N'Demo Seller',N'seller.a06@example.invalid',NULL,N'Seller');
DECLARE @seller int=SCOPE_IDENTITY();
INSERT dbo.[User](username,email,password,role) VALUES(N'Demo Admin',N'admin.a06@example.invalid',NULL,N'Admin');
INSERT dbo.Store(sellerId,storeName,description) VALUES(@seller,N'Demo Finds',N'Fictional store for local demonstrations.');
INSERT dbo.Address(userId,fullName,phone,street,city,state,country,isDefault)
VALUES(@buyer,N'Demo Buyer',NULL,N'123 Example Street',N'Demo City',N'Demo Region',N'Vietnam',1);
DECLARE @address int=SCOPE_IDENTITY();
INSERT dbo.Category(name) VALUES(N'Electronics');
DECLARE @electronics int=SCOPE_IDENTITY();
INSERT dbo.Category(name) VALUES(N'Home and Living');
DECLARE @home int=SCOPE_IDENTITY();
INSERT dbo.Product(title,description,price,categoryId,sellerId,isAuction)
VALUES(N'Wireless Headphones',N'Fictional fixed-price demo listing.',49.90,@electronics,@seller,0);
DECLARE @headphones int=SCOPE_IDENTITY();
INSERT dbo.Product(title,description,price,categoryId,sellerId,isAuction)
VALUES(N'Desk Lamp',N'Fictional fixed-price demo listing.',24.50,@home,@seller,0);
DECLARE @lamp int=SCOPE_IDENTITY();
INSERT dbo.Product(title,description,price,categoryId,sellerId,isAuction)
VALUES(N'USB-C Cable',N'Fictional fixed-price demo listing.',8.00,@electronics,@seller,0);
DECLARE @cable int=SCOPE_IDENTITY();
INSERT dbo.Inventory(productId,quantity,lastUpdated)
VALUES(@headphones,20,'20261001'),(@lamp,15,'20261001'),(@cable,50,'20261001');

DECLARE @orders TABLE(id int, status nvarchar(20));
INSERT dbo.OrderTable(buyerId,addressId,orderDate,totalPrice,status)
OUTPUT inserted.id,inserted.status INTO @orders
VALUES(@buyer,@address,'20260920',65.90,N'Pending'),
      (@buyer,@address,'20260921',65.90,N'Shipped'),
      (@buyer,@address,'20260922',65.90,N'Delivered'),
      (@buyer,@address,'20260923',65.90,N'Cancelled');
INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice)
SELECT id,@headphones,1,49.90 FROM @orders
UNION ALL SELECT id,@cable,2,8.00 FROM @orders;
INSERT dbo.Payment(orderId,userId,amount,method,status,paidAt)
SELECT id,@buyer,65.90,N'COD',
    CASE status WHEN N'Delivered' THEN N'Paid' WHEN N'Cancelled' THEN N'Cancelled' ELSE N'Pending' END,
    CASE WHEN status=N'Delivered' THEN CAST('20260925' AS datetime) END FROM @orders;
INSERT dbo.ShippingInfo(orderId,carrier,trackingNumber,status,estimatedArrival)
SELECT id,N'Demo Carrier',CONCAT(N'DEMO-A06-',id),status,'20260925'
FROM @orders WHERE status IN (N'Shipped',N'Delivered');
