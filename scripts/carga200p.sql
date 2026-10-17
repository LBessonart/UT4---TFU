CREATE TEMP TABLE tmp_pedidos AS
SELECT gen_random_uuid() AS id, g AS n FROM generate_series(1, 200000) g;

INSERT INTO "Pedidos" ("Id", "ClienteId", "Total", "FechaCreacion")
SELECT id, 'cliente-carga', 0, now() - (random() * interval '365 days')
FROM tmp_pedidos;

INSERT INTO "PedidoItems" ("Id", "PedidoId", "ProductoId", "Nombre", "Cantidad", "PrecioUnitario")
SELECT gen_random_uuid(),
       p.id,
       'prod-carga-' || lpad(((p.n * 7 + k) % 50 + 1)::text, 2, '0'),
       'Producto de carga',
       1 + (p.n + k) % 5,
       10.00 + ((p.n + k) % 50)
FROM tmp_pedidos p
CROSS JOIN LATERAL generate_series(1, CASE WHEN p.n % 2 = 0 THEN 3 ELSE 2 END) k;

ANALYZE "Pedidos";
ANALYZE "PedidoItems";