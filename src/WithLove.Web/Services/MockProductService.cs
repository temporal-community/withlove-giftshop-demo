using WithLove.Web.Models;

namespace WithLove.Web.Services;

public class MockProductService : IProductService
{
    private static readonly List<Category> _categories =
    [
        new Category
        {
            Id = 1,
            Name = "Cacao",
            Description = "Fine chocolates and confections crafted with care.",
            HeroTitle = "The Art of Cacao",
            HeroSubtitle = "Indulgent chocolates for every occasion.",
            Image = "https://lh3.googleusercontent.com/aida-public/AB6AXuAzcvDdOmei34ZM1V_zY2a2XiatABIwkxp_Cxrh6YySnZVo2KOBnLX3wwLw1kt3zfSI24jDs1QleYh46LlcNWfeU33EHM8U06fxTHv63uC9bC3Szjz-RdsGinfdq6-DdBnoKttYvdai4m7WhOmDKLkDAAAK1KfOndk6MM6TL_icYMeM-R4-0aIjs3yHReqaLhjqfv879STTwwbjI2eZeWUKjnIBadCSSdvUV-_zW5iEpyXyFaDPI--Bi4LGVpfBxP60TXGHVS6Hjlw",
            HeroImage = "https://lh3.googleusercontent.com/aida-public/AB6AXuAzcvDdOmei34ZM1V_zY2a2XiatABIwkxp_Cxrh6YySnZVo2KOBnLX3wwLw1kt3zfSI24jDs1QleYh46LlcNWfeU33EHM8U06fxTHv63uC9bC3Szjz-RdsGinfdq6-DdBnoKttYvdai4m7WhOmDKLkDAAAK1KfOndk6MM6TL_icYMeM-R4-0aIjs3yHReqaLhjqfv879STTwwbjI2eZeWUKjnIBadCSSdvUV-_zW5iEpyXyFaDPI--Bi4LGVpfBxP60TXGHVS6Hjlw",
            SubTypes = ["All Cacao", "Premium Collection", "Flavor Creations", "Floral Infusions", "Origin Stories", "Artisan Creations"],
            Occasions = ["Birthday", "Romance", "Thank You", "Celebration"]
        },
        new Category
        {
            Id = 2,
            Name = "Flora",
            Description = "Blooms that speak when words falter.",
            HeroTitle = "The Language of Flowers",
            HeroSubtitle = "Blooms that speak when words falter...",
            Image = "https://lh3.googleusercontent.com/aida-public/AB6AXuDmzy6t1dSZcdLn3nH-K_eIQEQVM0tHSW8OtotNY-BrMMoVo5vmarWAAtGilqv7tz04b5E8bKzrgoRT4ZrPvSdP_jjm_fbHjQRxK5rUOae2DoUEmxyeelcBCahqkfJKoHi7OU2p7JgEuhXBa4cH_7MLmCQqvCIHhCIFzH2-lQa0maPmWUNegRLI5oq5D97_mCK_rOz9a2hLW8CFvfXefkcBzankUXgwkD_fpAfwIpdDGn337E6DdOccUrhX5o_iJenZfqNxXBfIfsQ",
            HeroImage = "https://lh3.googleusercontent.com/aida-public/AB6AXuDmzy6t1dSZcdLn3nH-K_eIQEQVM0tHSW8OtotNY-BrMMoVo5vmarWAAtGilqv7tz04b5E8bKzrgoRT4ZrPvSdP_jjm_fbHjQRxK5rUOae2DoUEmxyeelcBCahqkfJKoHi7OU2p7JgEuhXBa4cH_7MLmCQqvCIHhCIFzH2-lQa0maPmWUNegRLI5oq5D97_mCK_rOz9a2hLW8CFvfXefkcBzankUXgwkD_fpAfwIpdDGn337E6DdOccUrhX5o_iJenZfqNxXBfIfsQ",
            SubTypes = ["All Flora", "Fresh Bouquets", "Dried Arrangements", "Potted Plants", "Single Stems"],
            Occasions = ["Romance", "Sympathy", "Celebration", "Just Because"]
        },
        new Category
        {
            Id = 3,
            Name = "Adornments",
            Description = "Jewellery and keepsakes that endure.",
            HeroTitle = "Wear Your Affection",
            HeroSubtitle = "Handcrafted jewellery for lasting moments.",
            Image = "https://lh3.googleusercontent.com/aida-public/AB6AXuAFTXBz1oBwjnwwCWSRlx8yw5epqJdFYBGoFDrSjirs2NJV6d1M4w_xeKzbZVOU7tsrnXfV8J_0loqf6U4_3Cc96Akyk3fvNsGYluAh93bDdMioiCVSoDj0vY6nu8og69QiRd8exPnK-AypYdLFp__jw52RyM0MWxwxqUOEslPXQ7je7Xu1GaDXIw17AFB2iArIn2bc-jU7XfPrXZ_N8CkVY51WDhviFUVQHQKxIYUcwuHc2k1szCdrUZUi1nIBbjn1q9hIfar6v60",
            HeroImage = "https://lh3.googleusercontent.com/aida-public/AB6AXuAFTXBz1oBwjnwwCWSRlx8yw5epqJdFYBGoFDrSjirs2NJV6d1M4w_xeKzbZVOU7tsrnXfV8J_0loqf6U4_3Cc96Akyk3fvNsGYluAh93bDdMioiCVSoDj0vY6nu8og69QiRd8exPnK-AypYdLFp__jw52RyM0MWxwxqUOEslPXQ7je7Xu1GaDXIw17AFB2iArIn2bc-jU7XfPrXZ_N8CkVY51WDhviFUVQHQKxIYUcwuHc2k1szCdrUZUi1nIBbjn1q9hIfar6v60",
            SubTypes = ["All Adornments", "Timeless Elegance", "Statement Pieces", "Everyday Elegance", "Timeless Keepsakes", "Luxury Collection"],
            Occasions = ["Romance", "Celebration", "Birthday", "Anniversary"]
        },
        new Category
        {
            Id = 4,
            Name = "Comfort",
            Description = "Plush companions and cosy gifts.",
            HeroTitle = "Warmth in Every Stitch",
            HeroSubtitle = "Soft gifts crafted with organic materials.",
            Image = "https://lh3.googleusercontent.com/aida-public/AB6AXuD79do_-XU9ao6htTTRauIo3Y7uky2lYFp3267-2ht7Ms9bdXp9PI9Iwq5xOL065tfifZqHtctatGJRcj2IFsklwa7dxy4ScpO2k1O9uYO1XtPvdY6v74vqpuCqYOekRszcvt-FI15Q3ZPjrGELoTJoTY6Ki6I5_KHLqb53V6kRj3Nc5xhKE1wbKDdBpq6zxCH_vhZITwm04V-sfaQUDF8Eg0JBOcwBA465P6cwEPz5RK9FOdpCUXvx8sMpQujQ2Ckc5cCrcm6886Y",
            HeroImage = "https://lh3.googleusercontent.com/aida-public/AB6AXuD79do_-XU9ao6htTTRauIo3Y7uky2lYFp3267-2ht7Ms9bdXp9PI9Iwq5xOL065tfifZqHtctatGJRcj2IFsklwa7dxy4ScpO2k1O9uYO1XtPvdY6v74vqpuCqYOekRszcvt-FI15Q3ZPjrGELoTJoTY6Ki6I5_KHLqb53V6kRj3Nc5xhKE1wbKDdBpq6zxCH_vhZITwm04V-sfaQUDF8Eg0JBOcwBA465P6cwEPz5RK9FOdpCUXvx8sMpQujQ2Ckc5cCrcm6886Y",
            SubTypes = ["All Comfort", "Plush Companions", "Cosy Sets", "Wellness", "Organic Gifts"],
            Occasions = ["Birthday", "New Baby", "Get Well", "Just Because"]
        },
        new Category
        {
            Id = 5,
            Name = "Words",
            Description = "Stationery and wrapping to complete the gift.",
            HeroTitle = "Every Word Counts",
            HeroSubtitle = "Handcrafted paper goods for your heartfelt messages.",
            Image = "https://lh3.googleusercontent.com/aida-public/AB6AXuA2Jvkxj-W1d-XP7jitQiaUO1LQyuI379et-WkWDDWVcxE6h4rjmqrYOpzqy75IwWGUqxmuutuB7r31NXGgYcWlMtZDAo4PBs_ray1I47Er54-4LT9O4zgZ_GyJB5D4NyV59s0nAtU3CmQyp2WVbM_nJSdIMTdyBbO2TzjjXo8bwGHt3vEVvwS_P_7cR43gvCDW1Y7Qi-CHxGSnFnRklBuQATNFy0SXoyiJc5RvxxT29GH44EzsCdcjqGz366NmAeW1OL2up43JNOY",
            HeroImage = "https://lh3.googleusercontent.com/aida-public/AB6AXuA2Jvkxj-W1d-XP7jitQiaUO1LQyuI379et-WkWDDWVcxE6h4rjmqrYOpzqy75IwWGUqxmuutuB7r31NXGgYcWlMtZDAo4PBs_ray1I47Er54-4LT9O4zgZ_GyJB5D4NyV59s0nAtU3CmQyp2WVbM_nJSdIMTdyBbO2TzjjXo8bwGHt3vEVvwS_P_7cR43gvCDW1Y7Qi-CHxGSnFnRklBuQATNFy0SXoyiJc5RvxxT29GH44EzsCdcjqGz366NmAeW1OL2up43JNOY",
            SubTypes = ["All Words", "Gift Wrapping", "Stationery", "Cards", "Personalised"],
            Occasions = ["Birthday", "Anniversary", "Sympathy", "Thank You"]
        },
        new Category
        {
            Id = 6,
            Name = "Sets",
            Description = "Curated gift sets for every relationship.",
            HeroTitle = "More Than the Sum of Parts",
            HeroSubtitle = "Thoughtfully assembled gift sets.",
            Image = "https://lh3.googleusercontent.com/aida-public/AB6AXuB81MODi-jnQjm7MrPoCdzeolOLVqsKZsMI5j8omu-Qa6Jt2JgHu0asgxdb8NDtabTQPiYUzi2CNpLGBKPiHUEKBbKw7HBTe6dAGqNT70KLqSRsc3NuloiCEgpendzj5MFEaLB6LzXiPk_8K3cNW2gxajFBJNaytN8GXZ6dtvJA4aa-jSk4HhmxZ3OWIdId-ksfvmNg2QBFqBy5V9qvvCQZzHB-k58bNQjPPmE4B5Hv__K3mao3Nkvq7OcvykCwBOuOXzJUmSQ8Xp8",
            HeroImage = "https://lh3.googleusercontent.com/aida-public/AB6AXuB81MODi-jnQjm7MrPoCdzeolOLVqsKZsMI5j8omu-Qa6Jt2JgHu0asgxdb8NDtabTQPiYUzi2CNpLGBKPiHUEKBbKw7HBTe6dAGqNT70KLqSRsc3NuloiCEgpendzj5MFEaLB6LzXiPk_8K3cNW2gxajFBJNaytN8GXZ6dtvJA4aa-jSk4HhmxZ3OWIdId-ksfvmNg2QBFqBy5V9qvvCQZzHB-k58bNQjPPmE4B5Hv__K3mao3Nkvq7OcvykCwBOuOXzJUmSQ8Xp8",
            SubTypes = ["All Sets", "Luxury Sets", "Hampers", "Spa Sets", "Gift Baskets"],
            Occasions = ["Birthday", "Christmas", "Romance", "Celebration"]
        }
    ];

    private static readonly List<Product> _products =
    [
        new Product
        {
            Id = 1,
            Name = "Velvet Crimson",
            Description = "For moments that require no words. Our signature crimson roses are hand-picked at dawn, preserving the morning dew and the deepest hues of affection.",
            Price = 89m,
            CategoryId = 2,
            SubCategory = "Fresh Bouquets",
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuBEB2L2RU7IQvQxpGwKCwE372ksSH0gEuSestVACG4TcNIMQNIfnzwapakiX5r_a_HhX4KSBqpFEzof_5Nsc6AkoWHQluJvmGS0iJifhuqOG6bZvR7v8j7giZCmICJ6RVtcIA86Vdg110gX99dnuzZdE8bXu2LbhAsFD93KuchfwYjWaY2BZKwgUN5rKvDCAly9EzDBKHIXp-r1NwMaKhtsKL_s5hn2tSdDz_ODRPeSzbGYQjnZiGGVmHyNROvYP1GY9O5vutjtJdw",
            Materials =
            [
                new ProductMaterial("local_florist", "Hand-Picked Crimson Roses"),
                new ProductMaterial("water_drop", "Morning Dew Preserved"),
                new ProductMaterial("eco", "Biodegradable Floral Foam"),
                new ProductMaterial("recycling", "Recycled Kraft Wrapping")
            ],
            Features =
            [
                new ProductFeature("wb_sunny", "Dawn Harvested", "Each stem is cut at first light to capture peak bloom colour and fragrance."),
                new ProductFeature("workspace_premium", "Florist Arranged", "Designed by our in-house florists with an eye for dramatic, romantic impact."),
                new ProductFeature("water_drop", "Hydration Pack", "Ships with a water sachet so blooms arrive perfectly fresh."),
                new ProductFeature("local_florist", "Same-Day Cut", "Roses are cut to order, never stored in cold rooms for days.")
            ],
            StoryTitle = "The Dawn Harvest",
            StoryDescription = "Long before the city stirs, our growers are already in the fields. Velvet Crimson was born from the belief that the very best roses belong to those golden minutes just after sunrise, when dew still clings to each petal and the colour is at its richest."
        },
        new Product
        {
            Id = 2,
            Name = "The Whisper",
            Description = "A gentle arrangement of dried florals and cream roses, perfect for quiet moments of heartfelt connection.",
            Price = 85m,

            CategoryId = 2,
            SubCategory = "Dried Arrangements",
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuB1eUtVySbrjZJwsn7WYr232qRZ0vRqSGdAgX-yCMlP54u2Tnp-LaFxazlal2JRYltiLPB6M7fJuTMtihOb9N7nU2Qj1r9yNqvMGaotLjd4gnNHtbNcERTMLzY5xq6pCc-r68dJmTeM0uMTAX-LFeTQP_RFzTvyTAIxxwmdz2Ve09iueAtiUoqSPpBC1PgMddY2ylX8IL5zph1eEOXOAg9qO6Hat8KEkLYLXR-RsoSK2Flf6Wb5B8pISf-ttu0uik9GKwvavDxuQm8",
            Materials =
            [
                new ProductMaterial("spa", "Preserved Cream Roses"),
                new ProductMaterial("grass", "Organic Pampas Grass"),
                new ProductMaterial("eco", "Sustainable Ceramics"),
                new ProductMaterial("recycling", "Recycled Silk Ribbon")
            ],
            Features =
            [
                new ProductFeature("wb_sunny", "Sun Dried", "Naturally preserved using traditional sun-drying techniques."),
                new ProductFeature("palette", "Hand Curated", "Every stem selected and arranged by our expert florists."),
                new ProductFeature("all_inclusive", "Everlasting", "Dried arrangements that last for months without any care."),
                new ProductFeature("workspace_premium", "Gift Ready", "Arrives in a signature keepsake box with a personalised card.")
            ],
            StoryTitle = "Crafted for Silence",
            StoryDescription = "The Whisper was inspired by the quiet moments just before dawn, when the world is still and every small thing feels precious. We gather stems at their peak and slowly coax them into permanent beauty — a reminder that some things are worth keeping forever."
        },
        new Product
        {
            Id = 3,
            Name = "Starlight Pendant",
            Description = "Crafted from recycled gold and ethically sourced gems, this constellation-inspired pendant brings the night sky a little closer.",
            Price = 220m,
            CategoryId = 3,
            SubCategory = "Timeless Elegance",
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuDWe8gFDpufP9975uuOWIUWA71Sm6HJ9jpna4sYrO8WrCd9hRMgbOhS_FBWX5RHw4NovK6Y65KezDfGO6J5WPERqpGyzFd0cM3pCQB08NHUZKm7p7dA4wmzPL3GrYueJwhjBMW56tUc7cxwAtDcV4a-UD5JjhKt7qWsAhXBELhZ5bA9M8XKqa4mVATrdtQn2O6SFkljvWtre0d6vtgs17qGFUkF_FsvS5eniGzYffFOXAFZLcSHw2d36Q0nMelfQFDkTH5S4qU4Tos",
            Materials =
            [
                new ProductMaterial("diamond", "Recycled 18k Gold"),
                new ProductMaterial("star", "Ethically Sourced White Sapphires"),
                new ProductMaterial("workspace_premium", "Sterling Silver Chain"),
                new ProductMaterial("verified", "Conflict-Free Certified")
            ],
            Features =
            [
                new ProductFeature("star", "Constellation Design", "Each pendant maps a real star cluster, making it uniquely meaningful."),
                new ProductFeature("diamond", "Hallmarked Gold", "Every piece carries an official assay hallmark for guaranteed quality."),
                new ProductFeature("workspace_premium", "Adjustable Length", "18–20 inch adjustable chain suits any neckline."),
                new ProductFeature("verified", "Ethical Sourcing", "Gems certified conflict-free with full supply-chain traceability.")
            ],
            StoryTitle = "Written in the Stars",
            StoryDescription = "On a clear night in the Scottish Highlands, our founder traced the stars and wondered why we couldn't carry them with us. Every Starlight Pendant is a tiny map of the cosmos, forged in recycled gold and set with sapphires as old as the universe itself."
        },
        new Product
        {
            Id = 4,
            Name = "Artisan Pralines",
            Description = "Hand-painted Belgian chocolates crafted by master chocolatiers using single-origin cacao.",
            Price = 45m,
            CategoryId = 1,
            SubCategory = "Artisan Creations",
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuDQln31ENPYQbq2nR1eIZbN_EyCfmyo7r6UMi8G0ltb1q9iL7E0tAd795rLiaRbbJN9bL3GYu_bBulaxCo1uob9yo3Z17cGCNGb7_Ak9B8P6MzOukuLjdQFjATzC6Zo2dDDlq0KCaNiuR6eaE_efKR1IWcyjJ9Z_UovEbnFd-uzSwWG29Y1b6W9wq_PkUYAAmor7XKnnysGAT7LxOsdt_5oe-OLel6H2xClFCcepgjGlP1zFJyIKB53sD21kOxbGkaweTGfCA_F1WY",
            Materials =
            [
                new ProductMaterial("spa", "Single-Origin Belgian Cacao"),
                new ProductMaterial("palette", "Edible Gold Leaf"),
                new ProductMaterial("eco", "Natural Cocoa Butter"),
                new ProductMaterial("auto_awesome", "House-Made Praline Paste")
            ],
            Features =
            [
                new ProductFeature("palette", "Hand Painted", "Each bonbon is individually painted by our chocolatiers using cocoa-based food colouring."),
                new ProductFeature("auto_awesome", "Master Crafted", "Created by chocolatiers with over two decades of confectionery experience."),
                new ProductFeature("workspace_premium", "Signature Box", "Presented in a magnetic-close gift box lined with custom tissue paper."),
                new ProductFeature("spa", "Single Origin", "Cacao traced to a single co-operative farm for transparent provenance.")
            ],
            StoryTitle = "Painted with Chocolate",
            StoryDescription = "In our Brussels atelier, each praline begins as a blank canvas. Our chocolatiers treat cacao like oil paint — layering colour, texture, and flavour until every piece is too beautiful to eat, and too delicious not to."
        },
        new Product
        {
            Id = 5,
            Name = "Velvet Paws Bear",
            Description = "A beautifully crafted plush bear made from 100% organic cotton, the perfect companion gift.",
            Price = 32m,
            CategoryId = 4,
            SubCategory = "Plush Companions",
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuC563J5c8lUAj-rUM2HmW5bK1zlZ30DSdBenbLAouvLn7lttTRlIXrwnplDH6-_N3AddmxCXzV6-ouHsJGDWXDYVLaQyeXwDYDFfpIIuEINTZF7t5Cq8J-ANcgYZWoblAYHOWlPe_QrqLpvkjlWnyvRthIDvR4q4zFf79x2W4DPaYG71XeY3PdJZdqqJZal6wk7YunGMbiMrJ7iNCCIvqTDwtqXPeiTkJV2B8rxfOBw0ju8sr-5mYbIm14TY4M1dqtGE3oYYdVRYvk",
            Materials =
            [
                new ProductMaterial("eco", "100% Organic Cotton Plush"),
                new ProductMaterial("spa", "Hypoallergenic Fill"),
                new ProductMaterial("recycling", "Recycled Polyester Stuffing"),
                new ProductMaterial("verified", "GOTS Certified Fabric")
            ],
            Features =
            [
                new ProductFeature("eco", "Organic & Safe", "Made with GOTS-certified organic cotton, safe for all ages including newborns."),
                new ProductFeature("spa", "Incredibly Soft", "Velvet-finish plush so impossibly soft it practically hugs back."),
                new ProductFeature("workspace_premium", "Heirloom Quality", "Triple-stitched seams and weighted paws built to last a lifetime of love."),
                new ProductFeature("verified", "Safety Tested", "Meets EN71 and ASTM safety standards for children's toys.")
            ],
            StoryTitle = "A Hug That Stays",
            StoryDescription = "Velvet Paws Bear was designed for those moments when you can't be there yourself. Sewn from organic cotton as soft as a whispered lullaby, this bear carries comfort across any distance — and keeps it safe long after the wrapping paper is forgotten."
        },
        new Product
        {
            Id = 6,
            Name = "Bespoke Wrapping",
            Description = "Sustainable Japanese Washi paper wrapping with a dried floral accent, elevating any gift.",
            Price = 15m,
            CategoryId = 5,
            SubCategory = "Gift Wrapping",
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuCXOMgfLAN299HXnFTq5MqxB_JFn9gIPvH2gwpSxwJ1jFNAnV7L4a10lo-nKWnD77PQUZ2JjaMh05XAhwY2a8uGuG9ntK79BgnoaQ7hQzdApuMtvig_v0H-4_7bNQ2eIRmW1qk1B5iJycrwhQlDg1A-p6ivT99YOhCNHkTwWS-VakfewSowim9jinZdq9JVt_V-EBoefcoBGuJBCg5Y5uXdSP4socWkyXbGmwTGjZPvwxgpAR2pGDUsdntMt0HHpPcxNzozVtyz5dI",
            Materials =
            [
                new ProductMaterial("recycling", "Japanese Washi Paper"),
                new ProductMaterial("local_florist", "Dried Floral Accent"),
                new ProductMaterial("eco", "Plant-Based Twine"),
                new ProductMaterial("spa", "Beeswax Seal")
            ],
            Features =
            [
                new ProductFeature("palette", "Artisan Folded", "Each wrap is hand-folded using traditional Japanese origami-inspired techniques."),
                new ProductFeature("eco", "Zero Plastic", "Entirely plastic-free — paper, twine, and wax only."),
                new ProductFeature("local_florist", "Floral Topped", "A hand-selected dried bloom is tucked into every wrap for a living finish."),
                new ProductFeature("recycling", "Compostable", "The entire wrap is home-compostable once the recipient is done admiring it.")
            ],
            StoryTitle = "The Unwrapping Moment",
            StoryDescription = "We believe the way a gift arrives is part of the gift itself. Our Bespoke Wrapping borrows from Japanese Tsutsumi — the art of wrapping as a gesture of respect — and adds a wild dried bloom because some rules are worth bending beautifully."
        },
        new Product
        {
            Id = 7,
            Name = "Morning Meadow",
            Description = "A vibrant seasonal wildflower bouquet capturing the essence of a fresh morning meadow.",
            Price = 55m,
            CategoryId = 2,
            SubCategory = "Fresh Bouquets",
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuDmzy6t1dSZcdLn3nH-K_eIQEQVM0tHSW8OtotNY-BrMMoVo5vmarWAAtGilqv7tz04b5E8bKzrgoRT4ZrPvSdP_jjm_fbHjQRxK5rUOae2DoUEmxyeelcBCahqkfJKoHi7OU2p7JgEuhXBa4cH_7MLmCQqvCIHhCIFzH2-lQa0maPmWUNegRLI5oq5D97_mCK_rOz9a2hLW8CFvfXefkcBzankUXgwkD_fpAfwIpdDGn337E6DdOccUrhX5o_iJenZfqNxXBfIfsQ",
            Materials =
            [
                new ProductMaterial("local_florist", "Seasonal Wildflowers"),
                new ProductMaterial("grass", "Meadow Grasses & Herbs"),
                new ProductMaterial("eco", "Organic Floral Foam"),
                new ProductMaterial("recycling", "Seed-Paper Wrap")
            ],
            Features =
            [
                new ProductFeature("wb_sunny", "Seasonal Selection", "Composition changes with the season for peak freshness and biodiversity."),
                new ProductFeature("local_florist", "Foraged Elements", "Wild grasses and herbs gathered from sustainable local meadows."),
                new ProductFeature("eco", "Plantable Wrap", "Seed-embedded wrapping paper that grows wildflowers when planted."),
                new ProductFeature("water_drop", "Care Guide", "Each bouquet includes a care card to keep blooms vibrant for up to ten days.")
            ],
            StoryTitle = "Bottled Countryside",
            StoryDescription = "Morning Meadow is our ode to the countryside at 7am — dewy, chaotic, impossibly alive. We gather whatever is blooming that week and let the season decide the palette. No two bouquets are ever quite the same, and that's exactly the point."
        },
        new Product
        {
            Id = 8,
            Name = "Silver Dollar",
            Description = "A fresh eucalyptus bundle with the natural silvery-green tones that bring calm to any space.",
            Price = 35m,
            CategoryId = 2,
            SubCategory = "Single Stems",
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuCXOMgfLAN299HXnFTq5MqxB_JFn9gIPvH2gwpSxwJ1jFNAnV7L4a10lo-nKWnD77PQUZ2JjaMh05XAhwY2a8uGuG9ntK79BgnoaQ7hQzdApuMtvig_v0H-4_7bNQ2eIRmW1qk1B5iJycrwhQlDg1A-p6ivT99YOhCNHkTwWS-VakfewSowim9jinZdq9JVt_V-EBoefcoBGuJBCg5Y5uXdSP4socWkyXbGmwTGjZPvwxgpAR2pGDUsdntMt0HHpPcxNzozVtyz5dI",
            Materials =
            [
                new ProductMaterial("spa", "Silver Dollar Eucalyptus"),
                new ProductMaterial("eco", "Organic Cotton Tie"),
                new ProductMaterial("recycling", "Recycled Paper Band"),
                new ProductMaterial("water_drop", "Hydration Tube")
            ],
            Features =
            [
                new ProductFeature("spa", "Natural Fragrance", "Fresh eucalyptus releases a clean, calming scent that fills the room naturally."),
                new ProductFeature("eco", "Air Purifying", "Eucalyptus is known for its air-cleansing properties and calming effect."),
                new ProductFeature("all_inclusive", "Dries Beautifully", "Hang upside down and it air-dries into a long-lasting sculptural bundle."),
                new ProductFeature("water_drop", "Shower Bundle", "Hang in the shower stream for a spa-like steam aromatherapy experience.")
            ],
            StoryTitle = "The Minimalist Gift",
            StoryDescription = "Sometimes one thing, done perfectly, says more than a hundred. Silver Dollar is a single eucalyptus bundle so thoughtfully curated it functions as gift, air freshener, and artwork all at once. It's our love letter to the power of restraint."
        },
        new Product
        {
            Id = 9,
            Name = "Pure Serenity",
            Description = "Imperial white lilies arranged with quiet elegance for moments of deep feeling.",
            Price = 72m,
            CategoryId = 2,
            SubCategory = "Fresh Bouquets",
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuDmzy6t1dSZcdLn3nH-K_eIQEQVM0tHSW8OtotNY-BrMMoVo5vmarWAAtGilqv7tz04b5E8bKzrgoRT4ZrPvSdP_jjm_fbHjQRxK5rUOae2DoUEmxyeelcBCahqkfJKoHi7OU2p7JgEuhXBa4cH_7MLmCQqvCIHhCIFzH2-lQa0maPmWUNegRLI5oq5D97_mCK_rOz9a2hLW8CFvfXefkcBzankUXgwkD_fpAfwIpdDGn337E6DdOccUrhX5o_iJenZfqNxXBfIfsQ",
            Materials =
            [
                new ProductMaterial("local_florist", "Imperial White Lilies"),
                new ProductMaterial("grass", "Soft Eucalyptus Sprigs"),
                new ProductMaterial("eco", "Cotton Gauze Wrap"),
                new ProductMaterial("spa", "White Satin Ribbon")
            ],
            Features =
            [
                new ProductFeature("local_florist", "Statement Blooms", "Large-headed imperial lilies chosen for their full, open faces and intense fragrance."),
                new ProductFeature("spa", "Intense Fragrance", "White lilies carry one of nature's most powerful natural perfumes."),
                new ProductFeature("palette", "Classic Styling", "Arranged in a structured, formal style that suits the most significant occasions."),
                new ProductFeature("workspace_premium", "Long Lasting", "Lilies continue to open over five to seven days, extending your gift's presence.")
            ],
            StoryTitle = "Silence Speaks",
            StoryDescription = "Pure Serenity was created for the moments when language falls short — the kind of occasions where only the most dignified, unhurried beauty will do. White lilies have carried meaning for centuries. We simply arrange them to carry yours."
        },
        new Product
        {
            Id = 10,
            Name = "Blush Peony",
            Description = "Soft pink double blooms arranged to capture the full, lush beauty of the peony.",
            Price = 78m,
            CategoryId = 2,
            SubCategory = "Fresh Bouquets",
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuDmzy6t1dSZcdLn3nH-K_eIQEQVM0tHSW8OtotNY-BrMMoVo5vmarWAAtGilqv7tz04b5E8bKzrgoRT4ZrPvSdP_jjm_fbHjQRxK5rUOae2DoUEmxyeelcBCahqkfJKoHi7OU2p7JgEuhXBa4cH_7MLmCQqvCIHhCIFzH2-lQa0maPmWUNegRLI5oq5D97_mCK_rOz9a2hLW8CFvfXefkcBzankUXgwkD_fpAfwIpdDGn337E6DdOccUrhX5o_iJenZfqNxXBfIfsQ",
            Materials =
            [
                new ProductMaterial("local_florist", "Garden Peonies"),
                new ProductMaterial("spa", "Soft Dusty Miller"),
                new ProductMaterial("grass", "Italian Ruscus"),
                new ProductMaterial("eco", "Biodegradable Wrap")
            ],
            Features =
            [
                new ProductFeature("local_florist", "Full Double Blooms", "Only fully double-headed peonies selected — the most generous, romantic form."),
                new ProductFeature("wb_sunny", "Short Season", "Available for just six weeks a year, making them a truly special gesture."),
                new ProductFeature("palette", "Blush Palette", "Curated in soft blush and cream tones to feel romantic without being showy."),
                new ProductFeature("water_drop", "Bud Stage Delivery", "Ships in tight bud so they open gloriously over three to five days at home.")
            ],
            StoryTitle = "Six Weeks a Year",
            StoryDescription = "Peonies are the great drama queens of the flower world — they arrive in a rush, bloom riotously, and vanish just as quickly. Blush Peony exists to bottle that fleeting extravagance and send it to someone who deserves a little theatrics in their week."
        },
        new Product
        {
            Id = 11,
            Name = "Letterpress Card",
            Description = "Letterpress-printed stationery on thick cotton paper, for words that deserve a beautiful vessel.",
            Price = 12m,
            CategoryId = 5,
            SubCategory = "Cards",
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuCL_ybLvB2bNntq94J5CiclR-h6BpYOmeyCzbDcsDbqkB5-BxZxhcGknI0FZdBApHBmvlV0kjLaxiinPfNg8iA9zFpujTBu-OazaWiId28TY5iWSm1_49JG1YVbHfLUyk6O2DS7nY0Auf7Lq6DGAfolkuzYiqZHKOpVV6Ix11D8_D7rjN2QawcBbGLDQ2N9UHEPnM8ddW-W5CNQGp0OpmQNfWYf_cdlwBLVrVNQinqyIGClL4_WWMzl7_i6SyPIKAVmtzDdu9WMqw8",
            Materials =
            [
                new ProductMaterial("palette", "300gsm Cotton Paper"),
                new ProductMaterial("eco", "Soy-Based Inks"),
                new ProductMaterial("spa", "Foil Embossed Detail"),
                new ProductMaterial("recycling", "FSC-Certified Stock")
            ],
            Features =
            [
                new ProductFeature("palette", "Letterpress Printed", "Pressed with a vintage Heidelberg platen press for a tactile, debossed finish."),
                new ProductFeature("eco", "Archival Quality", "Soy inks and cotton paper ensure the card will look beautiful for decades."),
                new ProductFeature("workspace_premium", "Blank Inside", "Generous white interior gives plenty of room for a heartfelt handwritten note."),
                new ProductFeature("star", "Keepsake Worthy", "Thick enough to frame — many recipients pin them up rather than put them away.")
            ],
            StoryTitle = "The Press That Started It All",
            StoryDescription = "We bought our 1960s letterpress at an estate sale in Edinburgh for £200 and a handshake. Every Letterpress Card since has been printed on the same machine, with the same satisfying thunk of steel meeting cotton. Old technology, new messages — it's a beautiful combination."
        },
        new Product
        {
            Id = 12,
            Name = "Dark Chocolate Truffle Collection",
            Description = "Assorted dark chocolate truffles dusted with cocoa powder and crushed nuts, a sophisticated indulgence.",
            Price = 52m,

            CategoryId = 1,
            SubCategory = "Premium Collection",
            ImageUrl = "https://plus.unsplash.com/premium_photo-1667031518595-9cb4b0d504ef",
            Materials =
            [
                new ProductMaterial("spa", "70% Valrhona Dark Chocolate"),
                new ProductMaterial("eco", "Raw Cacao Powder Dusting"),
                new ProductMaterial("auto_awesome", "Roasted Piedmont Hazelnuts"),
                new ProductMaterial("palette", "Pure Double Cream")
            ],
            Features =
            [
                new ProductFeature("workspace_premium", "Premium Valrhona", "Made exclusively with Valrhona Guanaja 70% for uncompromising depth of flavour."),
                new ProductFeature("palette", "Hand Rolled", "Each truffle hand-rolled by our chocolatiers and dusted individually for a rustic finish."),
                new ProductFeature("star", "Assorted Interiors", "Six flavour varieties including hazelnut praline, sea salt caramel, and espresso ganache."),
                new ProductFeature("spa", "Signature Box", "Presented in a matte black magnetic-close box with a printed flavour guide.")
            ],
            StoryTitle = "Darkness Made Beautiful",
            StoryDescription = "There's a moment in high-percentage dark chocolate — right around 70% — where bitter and sweet stop fighting and start dancing. Our truffle collection lives in that exact moment, hand-rolled by chocolatiers who take the phrase 'no shortcuts' very, very seriously."
        },
        new Product
        {
            Id = 13,
            Name = "Salted Caramel Bonbons",
            Description = "Smooth milk chocolate shells filled with tangy salted caramel and a touch of sea salt from Brittany.",
            Price = 38m,
            CategoryId = 1,
            SubCategory = "Flavor Creations",
            ImageUrl = "https://images.unsplash.com/photo-1679143121627-4dba2860ef50",
            Materials =
            [
                new ProductMaterial("spa", "40% Milk Chocolate"),
                new ProductMaterial("auto_awesome", "Brittany Fleur de Sel"),
                new ProductMaterial("eco", "Organic Cane Sugar"),
                new ProductMaterial("palette", "Fresh Normandy Butter")
            ],
            Features =
            [
                new ProductFeature("palette", "Hand Tempered", "Shells are hand-tempered daily for a clean snap and mirror-bright finish."),
                new ProductFeature("auto_awesome", "Sea Salt Finish", "Each bonbon finished with a single fleur de sel crystal for a satisfying crunch."),
                new ProductFeature("workspace_premium", "12-Piece Box", "Nestled in a gold-lined gift box with a ribbon pull for elegant presentation."),
                new ProductFeature("star", "Shelf Life", "Three-week shelf life without refrigeration — perfect for posting.")
            ],
            StoryTitle = "The Salt & Sweet Accord",
            StoryDescription = "Salted caramel didn't invent tension, but it perfected it. Our bonbons are built around that exact push and pull — silky Normandy butter caramel, warm milk chocolate shell, and one tiny crystal of Breton sea salt that makes the whole thing crackle to life."
        },
        new Product
        {
            Id = 14,
            Name = "Ruby Rose Chocolate",
            Description = "White chocolate infused with freeze-dried raspberries and rose petals, an elegant floral escape.",
            Price = 48m,
            CategoryId = 1,
            SubCategory = "Floral Infusions",
            ImageUrl = "https://images.unsplash.com/photo-1679143121360-d2f8e948e996",
            Materials =
            [
                new ProductMaterial("spa", "Single-Origin White Chocolate"),
                new ProductMaterial("local_florist", "Dried Rose Petals"),
                new ProductMaterial("auto_awesome", "Freeze-Dried Raspberries"),
                new ProductMaterial("palette", "Natural Rose Extract")
            ],
            Features =
            [
                new ProductFeature("local_florist", "Floral Infused", "Rose petals steeped overnight in cocoa butter to capture the full floral essence."),
                new ProductFeature("palette", "Rose-Tinted", "Natural colour from real raspberries — no artificial dyes ever."),
                new ProductFeature("auto_awesome", "Fruit Crunch", "Freeze-dried raspberry pieces add bright tartness and a satisfying texture contrast."),
                new ProductFeature("workspace_premium", "Romantic Gift", "Packaged in a blush-pink gift sleeve ideal for Valentine's, anniversaries, or just because.")
            ],
            StoryTitle = "A Garden in Every Bite",
            StoryDescription = "Ruby Rose began as a happy accident — a rose petal fell into a batch of white chocolate and, instead of fishing it out, our head chocolatier kept going. The result was too good to call a mistake. We've been making it ever since."
        },
        new Product
        {
            Id = 15,
            Name = "Single-Origin Ecuador",
            Description = "70% dark chocolate from Ecuador's finest cacao farms, with bright citrus notes and subtle earthiness.",
            Price = 28m,
            CategoryId = 1,
            SubCategory = "Origin Stories",
            ImageUrl = "https://images.unsplash.com/photo-1682120501920-7ce18b00237a",
            Materials =
            [
                new ProductMaterial("eco", "Arriba Nacional Cacao"),
                new ProductMaterial("verified", "Direct Trade Certified"),
                new ProductMaterial("recycling", "Compostable Wrapper"),
                new ProductMaterial("palette", "Unrefined Cane Sugar")
            ],
            Features =
            [
                new ProductFeature("eco", "Bean to Bar", "Entire process from raw cacao to finished bar happens in our workshop."),
                new ProductFeature("verified", "Direct Trade", "Cacao sourced directly from farmer co-operatives with full price transparency."),
                new ProductFeature("star", "Tasting Notes", "Comes with a printed tasting card describing provenance, flavour profile, and pairing ideas."),
                new ProductFeature("workspace_premium", "Minimal Ingredients", "Cacao, cane sugar, and nothing else — purity is the point.")
            ],
            StoryTitle = "The Farm Has a Name",
            StoryDescription = "Most chocolate hides its origins. Ours doesn't. Single-Origin Ecuador comes from Finca Los Ríos, a family co-operative in the Arriba region, where cacao has been grown the same way for four generations. When you eat this bar, you know exactly whose hands shaped it."
        },
        new Product
        {
            Id = 16,
            Name = "Honey Lavender Ganache",
            Description = "Rich dark chocolate ganache sweetened with local honey and infused with fragrant lavender from Provence.",
            Price = 42m,
            CategoryId = 1,
            SubCategory = "Artisan Creations",
            ImageUrl = "https://plus.unsplash.com/premium_photo-1716152295684-21731e330e36",
            Materials =
            [
                new ProductMaterial("spa", "Raw Wildflower Honey"),
                new ProductMaterial("local_florist", "Dried Provence Lavender"),
                new ProductMaterial("eco", "65% Dark Chocolate"),
                new ProductMaterial("auto_awesome", "Cold-Pressed Lavender Oil")
            ],
            Features =
            [
                new ProductFeature("local_florist", "Provençal Lavender", "Lavender sourced directly from a family farm in the Luberon region of Provence."),
                new ProductFeature("spa", "Raw Honey Sweetened", "Refined sugar replaced entirely with raw wildflower honey for a complex sweetness."),
                new ProductFeature("palette", "Ganache Centre", "Silky, slow-set ganache poured into moulded shells for a melt-in-the-mouth texture."),
                new ProductFeature("workspace_premium", "Seasonal Limited", "Made in small batches during lavender harvest season — quantities are genuinely limited.")
            ],
            StoryTitle = "The South of France in a Bonbon",
            StoryDescription = "Every summer our buyer drives to Provence and fills a van with dried lavender. We steep it in local honey for three weeks, fold it into dark ganache, and bottle the whole experience into a bonbon. It tastes exactly like the countryside smells at dusk — warm, floral, unhurried."
        },
        new Product
        {
            Id = 17,
            Name = "Emerald Gemstone Ring",
            Description = "14k gold band set with a brilliant emerald-cut emerald, surrounded by delicate diamond accents.",
            Price = 385m,
            CategoryId = 3,
            SubCategory = "Statement Pieces",
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuAFTXBz1oBwjnwwCWSRlx8yw5epqJdFYBGoFDrSjirs2NJV6d1M4w_xeKzbZVOU7tsrnXfV8J_0loqf6U4_3Cc96Akyk3fvNsGYluAh93bDdMioiCVSoDj0vY6nu8og69QiRd8exPnK-AypYdLFp__jw52RyM0MWxwxqUOEslPXQ7je7Xu1GaDXIw17AFB2iArIn2bc-jU7XfPrXZ_N8CkVY51WDhviFUVQHQKxIYUcwuHc2k1szCdrUZUi1nIBbjn1q9hIfar6v60",
            Materials =
            [
                new ProductMaterial("diamond", "14k Recycled Yellow Gold"),
                new ProductMaterial("star", "Colombian Emerald Centre Stone"),
                new ProductMaterial("verified", "Conflict-Free Diamonds"),
                new ProductMaterial("workspace_premium", "Rhodium Accent Finish")
            ],
            Features =
            [
                new ProductFeature("diamond", "Emerald Cut Stone", "Classic emerald cut maximises the stone's colour saturation and natural inclusions."),
                new ProductFeature("verified", "Certified Ethical", "Every stone accompanied by a Kimberley Process certificate and provenance documentation."),
                new ProductFeature("star", "Bespoke Sizing", "Available in full and half sizes; complimentary resizing within 60 days of purchase."),
                new ProductFeature("workspace_premium", "Luxury Packaging", "Delivered in a hand-stitched velvet ring box within a rigid outer gift case.")
            ],
            StoryTitle = "The Ring That Stops Time",
            StoryDescription = "There are moments you want to hold onto forever. The Emerald Gemstone Ring was designed to be worn on your hand during one of them. Forged from recycled gold, set with a Colombian emerald chosen for its depth of colour, and accented with diamonds because some things earn their extras."
        },
        new Product
        {
            Id = 18,
            Name = "Pearl Drops Earrings",
            Description = "Handcrafted gold earrings featuring lustrous South Sea pearls with delicate filigree details.",
            Price = 245m,
            CategoryId = 3,
            SubCategory = "Everyday Elegance",
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuAFTXBz1oBwjnwwCWSRlx8yw5epqJdFYBGoFDrSjirs2NJV6d1M4w_xeKzbZVOU7tsrnXfV8J_0loqf6U4_3Cc96Akyk3fvNsGYluAh93bDdMioiCVSoDj0vY6nu8og69QiRd8exPnK-AypYdLFp__jw52RyM0MWxwxqUOEslPXQ7je7Xu1GaDXIw17AFB2iArIn2bc-jU7XfPrXZ_N8CkVY51WDhviFUVQHQKxIYUcwuHc2k1szCdrUZUi1nIBbjn1q9hIfar6v60",
            Materials =
            [
                new ProductMaterial("diamond", "18k Yellow Gold"),
                new ProductMaterial("spa", "South Sea Cultured Pearls"),
                new ProductMaterial("verified", "AAA Grade Lustre"),
                new ProductMaterial("workspace_premium", "Hand-Filed Filigree")
            ],
            Features =
            [
                new ProductFeature("spa", "South Sea Pearls", "Cultured over four years in pristine Indonesian waters for superior size and lustre."),
                new ProductFeature("workspace_premium", "Filigree Setting", "Each earring features hand-filed gold wire work that is genuinely individual."),
                new ProductFeature("verified", "AAA Graded", "Only pearls graded AAA for surface quality and orient are used in this design."),
                new ProductFeature("star", "Comfortable Wear", "Lightweight construction and butterfly backs ensure all-day comfort without fatigue.")
            ],
            StoryTitle = "Four Years in the Making",
            StoryDescription = "A South Sea pearl takes four years to form inside a living oyster. We think that deserves respect. Pearl Drops Earrings are designed to hold that patient beauty without competing with it — delicate filigree gold work that says 'I'm here' while letting the pearl do all the talking."
        },
        new Product
        {
            Id = 19,
            Name = "Vintage Locket Necklace",
            Description = "Rose gold vintage-inspired locket engraved with botanical details, perfect for holding precious memories.",
            Price = 165m,

            CategoryId = 3,
            SubCategory = "Timeless Keepsakes",
            ImageUrl = "https://images.unsplash.com/photo-1589128777073-263566ae5e4d",
            Materials =
            [
                new ProductMaterial("diamond", "14k Rose Gold"),
                new ProductMaterial("local_florist", "Hand-Engraved Botanicals"),
                new ProductMaterial("spa", "Glass-Covered Interior"),
                new ProductMaterial("workspace_premium", "Satin Finish")
            ],
            Features =
            [
                new ProductFeature("local_florist", "Botanical Engraving", "Intricate fern and wildflower motifs engraved by hand using a burin tool."),
                new ProductFeature("spa", "Photo Locket", "Interior holds two miniature photos behind a scratch-resistant glass cover."),
                new ProductFeature("diamond", "Rose Gold Tone", "Warm blush tone of 14k rose gold flatters all skin tones beautifully."),
                new ProductFeature("workspace_premium", "Personalisation Option", "Request a custom engraving on the back — a date, initials, or a short phrase.")
            ],
            StoryTitle = "What You Carry With You",
            StoryDescription = "The locket is one of jewellery's oldest ideas: carry the people you love, always. We've updated the form with delicate hand-engraved botanicals and a rose gold finish, but the sentiment is unchanged — this is a piece built to hold your most important miniature worlds."
        },
        new Product
        {
            Id = 20,
            Name = "Sapphire Promise Bracelet",
            Description = "White gold bracelet adorned with deep blue sapphires alternating with brilliant white diamonds in a classic pattern.",
            Price = 425m,
            CategoryId = 3,
            SubCategory = "Luxury Collection",
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuAFTXBz1oBwjnwwCWSRlx8yw5epqJdFYBGoFDrSjirs2NJV6d1M4w_xeKzbZVOU7tsrnXfV8J_0loqf6U4_3Cc96Akyk3fvNsGYluAh93bDdMioiCVSoDj0vY6nu8og69QiRd8exPnK-AypYdLFp__jw52RyM0MWxwxqUOEslPXQ7je7Xu1GaDXIw17AFB2iArIn2bc-jU7XfPrXZ_N8CkVY51WDhviFUVQHQKxIYUcwuHc2k1szCdrUZUi1nIBbjn1q9hIfar6v60",
            Materials =
            [
                new ProductMaterial("diamond", "18k White Gold"),
                new ProductMaterial("star", "Ceylon Blue Sapphires"),
                new ProductMaterial("verified", "VS1 Clarity Diamonds"),
                new ProductMaterial("workspace_premium", "High-Polish Finish")
            ],
            Features =
            [
                new ProductFeature("star", "Alternating Stones", "Ceylon sapphires and VS1 diamonds set in perfect alternating rhythm around the bracelet."),
                new ProductFeature("diamond", "Secure Clasp", "Hidden box clasp with double safety catch — elegantly invisible from the top."),
                new ProductFeature("verified", "Certificate of Authenticity", "Comes with an independent gemmologist's certificate for every stone."),
                new ProductFeature("workspace_premium", "Signature Packaging", "Arrives in a rigid white leather presentation case with navy silk lining.")
            ],
            StoryTitle = "The Promise Worth Keeping",
            StoryDescription = "Some gifts mark a moment. The Sapphire Promise Bracelet marks a commitment — to a person, to a feeling, to the kind of love that earns its luxuries. Ceylon sapphires chosen for their particular, almost electric blue; diamonds chosen to make them shine even brighter. Together, they're a promise made visible."
        }
    ];

    private static readonly List<GiftEnhancement> _giftEnhancements =
    [
        new GiftEnhancement
        {
            Id = "bespoke-wrapping",
            Name = "Bespoke Wrapping",
            Description = "Sustainable Washi paper with dried floral accent.",
            Price = 15m,
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuA2Jvkxj-W1d-XP7jitQiaUO1LQyuI379et-WkWDDWVcxE6h4rjmqrYOpzqy75IwWGUqxmuutuB7r31NXGgYcWlMtZDAo4PBs_ray1I47Er54-4LT9O4zgZ_GyJB5D4NyV59s0nAtU3CmQyp2WVbM_nJSdIMTdyBbO2TzjjXo8bwGHt3vEVvwS_P_7cR43gvCDW1Y7Qi-CHxGSnFnRklBuQATNFy0SXoyiJc5RvxxT29GH44EzsCdcjqGz366NmAeW1OL2up43JNOY"
        },
        new GiftEnhancement
        {
            Id = "handwritten-card",
            Name = "Handwritten Card",
            Description = "Personalized message on thick cotton cardstock.",
            Price = 8m,
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuCXOMgfLAN299HXnFTq5MqxB_JFn9gIPvH2gwpSxwJ1jFNAnV7L4a10lo-nKWnD77PQUZ2JjaMh05XAhwY2a8uGuG9ntK79BgnoaQ7hQzdApuMtvig_v0H-4_7bNQ2eIRmW1qk1B5iJycrwhQlDg1A-p6ivT99YOhCNHkTwWS-VakfewSowim9jinZdq9JVt_V-EBoefcoBGuJBCg5Y5uXdSP4socWkyXbGmwTGjZPvwxgpAR2pGDUsdntMt0HHpPcxNzozVtyz5dI"
        }
    ];

    public Task<List<Product>> GetProductsAsync()
    {
        return Task.FromResult(_products.ToList());
    }

    public Task<List<Product>> GetFeaturedProductsAsync()
    {
        var featured = _products
            .Where(p => p.Id is 1 or 3)
            .ToList();
        return Task.FromResult(featured);
    }

    public Task<List<Product>> GetSmallLuxuriesAsync()
    {
        var smallLuxuries = _products
            .Where(p => p.Id is 4 or 5 or 6)
            .ToList();
        return Task.FromResult(smallLuxuries);
    }

    public Task<List<Category>> GetCategoriesAsync()
    {
        return Task.FromResult(_categories.ToList());
    }

    public Task<Category?> GetCategoryAsync(int categoryId)
    {
        var category = _categories.FirstOrDefault(c => c.Id == categoryId);
        return Task.FromResult(category);
    }

    public Task<List<Product>> GetProductsByCategoryAsync(int categoryId)
    {
        if (categoryId == 2)
        {
            var floraProducts = _products
                .Where(p => p.Id is 1 or 2 or 7 or 8 or 9 or 10)
                .ToList();
            return Task.FromResult(floraProducts);
        }

        var products = _products.Where(p => p.CategoryId == categoryId).ToList();
        return Task.FromResult(products);
    }

    public Task<Product?> GetProductAsync(int productId)
    {
        var product = _products.FirstOrDefault(p => p.Id == productId);
        return Task.FromResult(product);
    }

    public Task<List<Product>> GetRecommendationsAsync(int productId)
    {
        var recommendations = _products
            .Where(p => p.Id is 4 or 3 or 6 or 11)
            .ToList();
        return Task.FromResult(recommendations);
    }

    public Task<List<Product>> SearchProductsAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult(new List<Product>());

        var results = _products
            .Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult(results);
    }

    public Task<List<GiftEnhancement>> GetGiftEnhancementsAsync()
    {
        return Task.FromResult(_giftEnhancements.ToList());
    }
}
