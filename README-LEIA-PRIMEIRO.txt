FÁBIO REPRESENTAÇÕES — DEMONSTRAÇÃO MULTI-REPRESENTANTE
======================================================

IMPORTANTE
- Este pacote é uma demonstração de desenvolvimento, não uma implantação pronta para cobrar mensalidades ou receber dados reais.
- O sistema grava dados em Dados/plataforma.json. Ele NÃO conecta nem modifica seu banco MySQL antigo. Seus projetos e banco antigos devem ser mantidos intactos.
- Ainda não foi possível compilar este pacote neste ambiente porque o .NET SDK não está instalado aqui. Abra no Visual Studio e compile/teste antes de apresentar como funcional.
- O pedido impresso é PEDIDO COMERCIAL/orçamento, não emite NF-e.
- Não use dados pessoais reais de clientes durante o teste.

REQUISITOS
- Visual Studio 2026 com ASP.NET Core / .NET 10 SDK.

COMO ABRIR
1. Extraia o ZIP para uma pasta nova, sem substituir o projeto antigo.
2. Abra FabioRepresentacoes.csproj no Visual Studio.
3. Aguarde o restore e compile (Build > Build Solution).
4. Execute o projeto. O endereço local esperado é http://localhost:5088.
5. Se o Windows bloquear a DLL, não desative a segurança do Windows. Anote o erro e peça ajuda para revisar a política de execução.

CATÁLOGO DE DEMONSTRAÇÃO
- http://localhost:5088/index.html?loja=fabio
- O cliente escolhe produtos, quantidades e envia um pedido.
- O estoque baixa ao registrar o pedido. O servidor confere estoque antes de aceitar.

ACESSO DO REPRESENTANTE FÁBIO (DEMONSTRAÇÃO)
- http://localhost:5088/admin.html?loja=fabio
- E-mail: fabio@demo.local
- Senha: Demo@12345

ADMINISTRADOR DA PLATAFORMA (CRIAR OUTROS REPRESENTANTES)
- http://localhost:5088/platform.html
- E-mail: admin@fabio.local
- Senha: Trocar@12345
- Crie uma loja com nome comercial, slug/endereço, nome do representante, e-mail e senha inicial.
- O representante acessa /admin.html?loja=slug e o catálogo público fica em /index.html?loja=slug.
- Troque as senhas de demonstração antes de qualquer uso fora do notebook.

RECURSOS IMPLEMENTADOS NESTA DEMONSTRAÇÃO
- Uma plataforma com várias lojas/representantes e separação de dados por TenantId.
- Nome comercial, descrição, telefone e logotipo por representante.
- Catálogo, categorias, código de produto, preço, descrição e foto.
- Estoque atual e estoque mínimo; alerta visual quando estoque <= mínimo.
- Entrada de mercadoria soma unidades ao estoque existente.
- Pedido confere disponibilidade e baixa o estoque.
- Cancelamento de pedido repõe o estoque; reabertura tenta baixar novamente.
- Painel do representante com pedidos, clientes, produtos e resumo.
- Status do pedido, lixeira, restauração e exclusão definitiva.
- Pedido comercial para imprimir / salvar em PDF e resumo por WhatsApp.
- Senhas armazenadas com hash; cookies HttpOnly; APIs administrativas exigem autenticação e verificam o tenant.

LIMITAÇÕES QUE PRECISAM SER RESOLVIDAS ANTES DE VENDER/USAR COM CLIENTES REAIS
- O armazenamento atual é um JSON simples e adequado apenas para demonstração local; não é um banco multiusuário de produção.
- Não há cadastro/login de cliente com histórico privado; o pedido é feito como visitante.
- Não há recuperação de senha, MFA, rate limiting, trilha de auditoria completa ou fluxo de assinatura/pagamento.
- Não há migração automática dos produtos e pedidos do MySQL antigo.
- Uploads precisam de validação de conteúdo e armazenamento mais seguro para produção.
- Para publicar na internet, usar banco de dados real, HTTPS, backups, segredos fora do código, proteção CSRF e testes de isolamento entre representantes.
- A versão local usa HTTP. Não exponha à internet e não compartilhe o link público antes de preparar hospedagem e segurança.

DADOS
- Todos os dados novos ficam na pasta Dados/ e as fotos em wwwroot/uploads/.
- Faça cópia de segurança dessa pasta antes de qualquer alteração.
