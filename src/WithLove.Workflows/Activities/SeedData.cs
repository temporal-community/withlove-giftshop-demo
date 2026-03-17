using WithLove.Data.Models;

namespace WithLove.Workflows.Activities;

internal static class SeedData
{
    public static List<Category> GetSeedCategories() =>
    [
        new()
        {
            Name = "Cacao",
            Description = "Fine chocolates and confections crafted with care.",
            HeroTitle = "The Art of Cacao",
            HeroSubtitle = "Indulgent chocolates for every occasion.",
            Image = "https://lh3.googleusercontent.com/aida-public/AB6AXuAzcvDdOmei34ZM1V_zY2a2XiatABIwkxp_Cxrh6YySnZVo2KOBnLX3wwLw1kt3zfSI24jDs1QleYh46LlcNWfeU33EHM8U06fxTHv63uC9bC3Szjz-RdsGinfdq6-DdBnoKttYvdai4m7WhOmDKLkDAAAK1KfOndk6MM6TL_icYMeM-R4-0aIjs3yHReqaLhjqfv879STTwwbjI2eZeWUKjnIBadCSSdvUV-_zW5iEpyXyFaDPI--Bi4LGVpfBxP60TXGHVS6Hjlw",
            HeroImage = "https://lh3.googleusercontent.com/aida-public/AB6AXuAzcvDdOmei34ZM1V_zY2a2XiatABIwkxp_Cxrh6YySnZVo2KOBnLX3wwLw1kt3zfSI24jDs1QleYh46LlcNWfeU33EHM8U06fxTHv63uC9bC3Szjz-RdsGinfdq6-DdBnoKttYvdai4m7WhOmDKLkDAAAK1KfOndk6MM6TL_icYMeM-R4-0aIjs3yHReqaLhjqfv879STTwwbjI2eZeWUKjnIBadCSSdvUV-_zW5iEpyXyFaDPI--Bi4LGVpfBxP60TXGHVS6Hjlw",
            SubTypes = ["Premium Collection", "Flavor Creations", "Floral Infusions", "Origin Stories", "Artisan Creations"],
            Occasions = ["Birthday", "Romance", "Thank You", "Celebration"]
        },
        new()
        {
            Name = "Flora",
            Description = "Blooms that speak when words falter.",
            HeroTitle = "The Language of Flowers",
            HeroSubtitle = "Blooms that speak when words falter...",
            Image = "https://lh3.googleusercontent.com/aida-public/AB6AXuDmzy6t1dSZcdLn3nH-K_eIQEQVM0tHSW8OtotNY-BrMMoVo5vmarWAAtGilqv7tz04b5E8bKzrgoRT4ZrPvSdP_jjm_fbHjQRxK5rUOae2DoUEmxyeelcBCahqkfJKoHi7OU2p7JgEuhXBa4cH_7MLmCQqvCIHhCIFzH2-lQa0maPmWUNegRLI5oq5D97_mCK_rOz9a2hLW8CFvfXefkcBzankUXgwkD_fpAfwIpdDGn337E6DdOccUrhX5o_iJenZfqNxXBfIfsQ",
            HeroImage = "https://lh3.googleusercontent.com/aida-public/AB6AXuDmzy6t1dSZcdLn3nH-K_eIQEQVM0tHSW8OtotNY-BrMMoVo5vmarWAAtGilqv7tz04b5E8bKzrgoRT4ZrPvSdP_jjm_fbHjQRxK5rUOae2DoUEmxyeelcBCahqkfJKoHi7OU2p7JgEuhXBa4cH_7MLmCQqvCIHhCIFzH2-lQa0maPmWUNegRLI5oq5D97_mCK_rOz9a2hLW8CFvfXefkcBzankUXgwkD_fpAfwIpdDGn337E6DdOccUrhX5o_iJenZfqNxXBfIfsQ",
            SubTypes = ["Fresh Bouquets", "Dried Arrangements", "Potted Plants", "Single Stems"],
            Occasions = ["Romance", "Sympathy", "Celebration", "Just Because"]
        },
        new()
        {
            Name = "Adornments",
            Description = "Jewellery and keepsakes that endure.",
            HeroTitle = "Wear Your Affection",
            HeroSubtitle = "Handcrafted jewellery for lasting moments.",
            Image = "https://lh3.googleusercontent.com/aida-public/AB6AXuAFTXBz1oBwjnwwCWSRlx8yw5epqJdFYBGoFDrSjirs2NJV6d1M4w_xeKzbZVOU7tsrnXfV8J_0loqf6U4_3Cc96Akyk3fvNsGYluAh93bDdMioiCVSoDj0vY6nu8og69QiRd8exPnK-AypYdLFp__jw52RyM0MWxwxqUOEslPXQ7je7Xu1GaDXIw17AFB2iArIn2bc-jU7XfPrXZ_N8CkVY51WDhviFUVQHQKxIYUcwuHc2k1szCdrUZUi1nIBbjn1q9hIfar6v60",
            HeroImage = "https://lh3.googleusercontent.com/aida-public/AB6AXuAFTXBz1oBwjnwwCWSRlx8yw5epqJdFYBGoFDrSjirs2NJV6d1M4w_xeKzbZVOU7tsrnXfV8J_0loqf6U4_3Cc96Akyk3fvNsGYluAh93bDdMioiCVSoDj0vY6nu8og69QiRd8exPnK-AypYdLFp__jw52RyM0MWxwxqUOEslPXQ7je7Xu1GaDXIw17AFB2iArIn2bc-jU7XfPrXZ_N8CkVY51WDhviFUVQHQKxIYUcwuHc2k1szCdrUZUi1nIBbjn1q9hIfar6v60",
            SubTypes = ["Timeless Elegance", "Statement Pieces", "Everyday Elegance", "Timeless Keepsakes", "Luxury Collection"],
            Occasions = ["Romance", "Celebration", "Birthday", "Anniversary"]
        },
        new()
        {
            Name = "Comfort",
            Description = "Plush companions and cosy gifts.",
            HeroTitle = "Warmth in Every Stitch",
            HeroSubtitle = "Soft gifts crafted with organic materials.",
            Image = "https://lh3.googleusercontent.com/aida-public/AB6AXuD79do_-XU9ao6htTTRauIo3Y7uky2lYFp3267-2ht7Ms9bdXp9PI9Iwq5xOL065tfifZqHtctatGJRcj2IFsklwa7dxy4ScpO2k1O9uYO1XtPvdY6v74vqpuCqYOekRszcvt-FI15Q3ZPjrGELoTJoTY6Ki6I5_KHLqb53V6kRj3Nc5xhKE1wbKDdBpq6zxCH_vhZITwm04V-sfaQUDF8Eg0JBOcwBA465P6cwEPz5RK9FOdpCUXvx8sMpQujQ2Ckc5cCrcm6886Y",
            HeroImage = "https://lh3.googleusercontent.com/aida-public/AB6AXuD79do_-XU9ao6htTTRauIo3Y7uky2lYFp3267-2ht7Ms9bdXp9PI9Iwq5xOL065tfifZqHtctatGJRcj2IFsklwa7dxy4ScpO2k1O9uYO1XtPvdY6v74vqpuCqYOekRszcvt-FI15Q3ZPjrGELoTJoTY6Ki6I5_KHLqb53V6kRj3Nc5xhKE1wbKDdBpq6zxCH_vhZITwm04V-sfaQUDF8Eg0JBOcwBA465P6cwEPz5RK9FOdpCUXvx8sMpQujQ2Ckc5cCrcm6886Y",
            SubTypes = ["Plush Companions", "Cosy Sets", "Wellness", "Organic Gifts"],
            Occasions = ["Birthday", "New Baby", "Get Well", "Just Because"]
        },
        new()
        {
            Name = "Words",
            Description = "Stationery and wrapping to complete the gift.",
            HeroTitle = "Every Word Counts",
            HeroSubtitle = "Handcrafted paper goods for your heartfelt messages.",
            Image = "https://lh3.googleusercontent.com/aida-public/AB6AXuA2Jvkxj-W1d-XP7jitQiaUO1LQyuI379et-WkWDDWVcxE6h4rjmqrYOpzqy75IwWGUqxmuutuB7r31NXGgYcWlMtZDAo4PBs_ray1I47Er54-4LT9O4zgZ_GyJB5D4NyV59s0nAtU3CmQyp2WVbM_nJSdIMTdyBbO2TzjjXo8bwGHt3vEVvwS_P_7cR43gvCDW1Y7Qi-CHxGSnFnRklBuQATNFy0SXoyiJc5RvxxT29GH44EzsCdcjqGz366NmAeW1OL2up43JNOY",
            HeroImage = "https://lh3.googleusercontent.com/aida-public/AB6AXuA2Jvkxj-W1d-XP7jitQiaUO1LQyuI379et-WkWDDWVcxE6h4rjmqrYOpzqy75IwWGUqxmuutuB7r31NXGgYcWlMtZDAo4PBs_ray1I47Er54-4LT9O4zgZ_GyJB5D4NyV59s0nAtU3CmQyp2WVbM_nJSdIMTdyBbO2TzjjXo8bwGHt3vEVvwS_P_7cR43gvCDW1Y7Qi-CHxGSnFnRklBuQATNFy0SXoyiJc5RvxxT29GH44EzsCdcjqGz366NmAeW1OL2up43JNOY",
            SubTypes = ["Gift Wrapping", "Stationery", "Cards", "Personalised"],
            Occasions = ["Birthday", "Anniversary", "Sympathy", "Thank You"]
        },
        new()
        {
            Name = "Sets",
            Description = "Curated gift sets for every relationship.",
            HeroTitle = "More Than the Sum of Parts",
            HeroSubtitle = "Thoughtfully assembled gift sets.",
            Image = "https://lh3.googleusercontent.com/aida-public/AB6AXuB81MODi-jnQjm7MrPoCdzeolOLVqsKZsMI5j8omu-Qa6Jt2JgHu0asgxdb8NDtabTQPiYUzi2CNpLGBKPiHUEKBbKw7HBTe6dAGqNT70KLqSRsc3NuloiCEgpendzj5MFEaLB6LzXiPk_8K3cNW2gxajFBJNaytN8GXZ6dtvJA4aa-jSk4HhmxZ3OWIdId-ksfvmNg2QBFqBy5V9qvvCQZzHB-k58bNQjPPmE4B5Hv__K3mao3Nkvq7OcvykCwBOuOXzJUmSQ8Xp8",
            HeroImage = "https://lh3.googleusercontent.com/aida-public/AB6AXuB81MODi-jnQjm7MrPoCdzeolOLVqsKZsMI5j8omu-Qa6Jt2JgHu0asgxdb8NDtabTQPiYUzi2CNpLGBKPiHUEKBbKw7HBTe6dAGqNT70KLqSRsc3NuloiCEgpendzj5MFEaLB6LzXiPk_8K3cNW2gxajFBJNaytN8GXZ6dtvJA4aa-jSk4HhmxZ3OWIdId-ksfvmNg2QBFqBy5V9qvvCQZzHB-k58bNQjPPmE4B5Hv__K3mao3Nkvq7OcvykCwBOuOXzJUmSQ8Xp8",
            SubTypes = ["Luxury Sets", "Hampers", "Spa Sets", "Gift Baskets"],
            Occasions = ["Birthday", "Christmas", "Romance", "Celebration"]
        }
    ];
    
    public static List<Product> GetSeedProducts(Dictionary<string, Category> categoryByName)
    {
        Category Cat(string name) => categoryByName[name];

        return
        [
            new()
            {
                Name = "Velvet Crimson",
                Description = "For moments that require no words. Our signature crimson roses are hand-picked at dawn, preserving the morning dew and the deepest hues of affection.",
                Price = 89.00m,
                Category = Cat("Flora"),
                SubCategory = "Fresh Bouquets",
                ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuBEB2L2RU7IQvQxpGwKCwE372ksSH0gEuSestVACG4TcNIMQNIfnzwapakiX5r_a_HhX4KSBqpFEzof_5Nsc6AkoWHQluJvmGS0iJifhuqOG6bZvR7v8j7giZCmICJ6RVtcIA86Vdg110gX99dnuzZdE8bXu2LbhAsFD93KuchfwYjWaY2BZKwgUN5rKvDCAly9EzDBKHIXp-r1NwMaKhtsKL_s5hn2tSdDz_ODRPeSzbGYQjnZiGGVmHyNROvYP1GY9O5vutjtJdw",
                Materials =
                [
                    new() { Icon = "spa", Name = "Crimson Garden Roses" },
                    new() { Icon = "grass", Name = "Eucalyptus Sprigs" },
                    new() { Icon = "eco", Name = "Seasonal Greenery" },
                    new() { Icon = "recycling", Name = "Biodegradable Wrap" }
                ],
                Features =
                [
                    new() { Icon = "wb_sunny", Title = "Hand-Picked at Dawn", Description = "Selected at the peak of freshness before the morning heat sets in." },
                    new() { Icon = "local_florist", Title = "Expert Arrangement", Description = "Assembled by trained florists with an eye for balance and beauty." },
                    new() { Icon = "water_drop", Title = "Fresh-Cut Guarantee", Description = "Cut same-day and kept in water until dispatch." },
                    new() { Icon = "eco", Title = "Eco-Friendly Packaging", Description = "Wrapped in recycled kraft paper with no single-use plastics." }
                ],
                StoryTitle = "Born at Dawn",
                StoryDescription = "Hand-picked at first light, when roses hold their deepest colour and freshest scent. No alarm clocks were harmed in the making of this bouquet."
            },
            new()
            {
                Name = "The Whisper",
                Description = "A gentle arrangement of dried florals and cream roses, perfect for quiet moments...",
                Price = 85.00m,
                Category = Cat("Flora"),
                SubCategory = "Dried Arrangements",
                ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuB1eUtVySbrjZJwsn7WYr232qRZ0vRqSGdAgX-yCMlP54u2Tnp-LaFxazlal2JRYltiLPB6M7fJuTMtihOb9N7nU2Qj1r9yNqvMGaotLjd4gnNHtbNcERTMLzY5xq6pCc-r68dJmTeM0uMTAX-LFeTQP_RFzTvyTAIxxwmdz2Ve09iueAtiUoqSPpBC1PgMddY2ylX8IL5zph1eEOXOAg9qO6Hat8KEkLYLXR-RsoSK2Flf6Wb5B8pISf-ttu0uik9GKwvavDxuQm8",
                Materials =
                [
                    new() { Icon = "spa", Name = "Preserved Cream Roses" },
                    new() { Icon = "grass", Name = "Organic Pampas Grass" },
                    new() { Icon = "eco", Name = "Sustainable Ceramics" },
                    new() { Icon = "recycling", Name = "Recycled Silk Ribbon" }
                ],
                Features =
                [
                    new() { Icon = "wb_sunny", Title = "Sun Dried", Description = "Naturally preserved using traditional sun-drying techniques." },
                    new() { Icon = "palette", Title = "Hand Curated", Description = "Every stem selected and arranged by our expert florists." },
                    new() { Icon = "all_inclusive", Title = "Everlasting", Description = "Dried arrangements that last for months without any care." }
                ],
                StoryTitle = "Crafted for Silence",
                StoryDescription = "The Whisper was inspired by the quiet moments just before dawn, when the world holds its breath and beauty needs no explanation."
            },
            new()
            {
                Name = "Starlight Pendant",
                Description = "Crafted from recycled gold and ethically sourced gems...",
                Price = 220.00m,
                Category = Cat("Adornments"),
                SubCategory = "Timeless Elegance",
                ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuDWe8gFDpufP9975uuOWIUWA71Sm6HJ9jpna4sYrO8WrCd9hRMgbOhS_FBWX5RHw4NovK6Y65KezDfGO6J5WPERqpGyzFd0cM3pCQB08NHUZKm7p7dA4wmzPL3GrYueJwhjBMW56tUc7cxwAtDcV4a-UD5JjhKt7qWsAhXBELhZ5bA9M8XKqa4mVATrdtQn2O6SFkljvWtre0d6vtgs17qGFUkF_FsvS5eniGzYffFOXAFZLcSHw2d36Q0nMelfQFDkTH5S4qU4Tos",
                Materials =
                [
                    new() { Icon = "diamond", Name = "Recycled 18k Gold" },
                    new() { Icon = "spa", Name = "Ethically Sourced Gemstone" },
                    new() { Icon = "auto_awesome", Name = "Hypoallergenic Setting" },
                    new() { Icon = "eco", Name = "Conflict-Free Certified" }
                ],
                Features =
                [
                    new() { Icon = "verified", Title = "Ethically Sourced", Description = "Every gem traced to its origin and certified conflict-free." },
                    new() { Icon = "eco", Title = "Recycled Metal", Description = "Crafted from reclaimed gold, reducing mining impact significantly." },
                    new() { Icon = "workspace_premium", Title = "Hallmarked Gold", Description = "18k gold hallmarked for guaranteed purity." },
                    new() { Icon = "auto_awesome", Title = "Handcrafted Finish", Description = "Each setting shaped and polished by skilled jewellers." }
                ],
                StoryTitle = "Forged from the Earth",
                StoryDescription = "Recycled gold meets ethically sourced gems in a pendant that proves luxury and conscience can coexist beautifully on the same chain."
            },
            new()
            {
                Name = "Artisan Pralines",
                Description = "Hand-painted Belgian chocolates crafted by master chocolatiers using single-origin cacao.",
                Price = 45.00m,
                Category = Cat("Cacao"),
                SubCategory = "Artisan Creations",
                ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuDQln31ENPYQbq2nR1eIZbN_EyCfmyo7r6UMi8G0ltb1q9iL7E0tAd795rLiaRbbJN9bL3GYu_bBulaxCo1uob9yo3Z17cGCNGb7_Ak9B8P6MzOukuLjdQFjATzC6Zo2dDDlq0KCaNiuR6eaE_efKR1IWcyjJ9Z_UovEbnFd-uzSwWG29Y1b6W9wq_PkUYAAmor7XKnnysGAT7LxOsdt_5oe-OLel6H2xClFCcepgjGlP1zFJyIKB53sD21kOxbGkaweTGfCA_F1WY",
                Materials =
                [
                    new() { Icon = "spa", Name = "Single-Origin Cacao" },
                    new() { Icon = "eco", Name = "Natural Cocoa Powder" },
                    new() { Icon = "water_drop", Name = "Pure Cocoa Butter" },
                    new() { Icon = "grass", Name = "Organic Sugar" }
                ],
                Features =
                [
                    new() { Icon = "palette", Title = "Hand-Painted", Description = "Each piece individually decorated by master chocolatiers." },
                    new() { Icon = "workspace_premium", Title = "Small Batch Made", Description = "Produced in limited quantities to ensure quality control." },
                    new() { Icon = "star", Title = "Single-Origin Cacao", Description = "Sourced from one farm for a consistent, distinctive flavour." },
                    new() { Icon = "eco", Title = "All-Natural Ingredients", Description = "No artificial colours, flavours, or preservatives." }
                ],
                StoryTitle = "Painted with Cocoa",
                StoryDescription = "Each praline is hand-painted by Belgian chocolatiers who take their craft very seriously and their lunch breaks even more so."
            },
            new()
            {
                Name = "Velvet Paws Bear",
                Description = "A beautifully crafted plush bear made from 100% organic cotton, the perfect companion gift.",
                Price = 32.00m,
                Category = Cat("Comfort"),
                SubCategory = "Plush Companions",
                ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuC563J5c8lUAj-rUM2HmW5bK1zlZ30DSdBenbLAouvLn7lttTRlIXrwnplDH6-_N3AddmxCXzV6-ouHsJGDWXDYVLaQyeXwDYDFfpIIuEINTZF7t5Cq8J-ANcgYZWoblAYHOWlPe_QrqLpvkjlWnyvRthIDvR4q4zFf79x2W4DPaYG71XeY3PdJZdqqJZal6wk7YunGMbiMrJ7iNCCIvqTDwtqXPeiTkJV2B8rxfOBw0ju8sr-5mYbIm14TY4M1dqtGE3oYYdVRYvk",
                Materials =
                [
                    new() { Icon = "eco", Name = "100% Organic Cotton" },
                    new() { Icon = "grass", Name = "Recycled Fibrefill" },
                    new() { Icon = "spa", Name = "Natural Dyes" },
                    new() { Icon = "recycling", Name = "Sustainable Packaging" }
                ],
                Features =
                [
                    new() { Icon = "verified", Title = "Safety Certified", Description = "Made to EN71 toy safety standards, suitable for all ages." },
                    new() { Icon = "eco", Title = "Organic Certified", Description = "GOTS-certified organic cotton throughout." },
                    new() { Icon = "water_drop", Title = "Machine Washable", Description = "Gentle wash at 30°C keeps Velvet Paws fresh and huggable." },
                    new() { Icon = "workspace_premium", Title = "Handstitched Detail", Description = "Individually stitched facial features for lasting character." }
                ],
                StoryTitle = "The Bear Who Listens",
                StoryDescription = "Handstitched from organic cotton and stuffed with recycled fibre, Velvet Paws is the world's most patient listener. Never judges. Always available."
            },
            new()
            {
                Name = "Bespoke Wrapping",
                Description = "Sustainable Japanese Washi paper wrapping with a dried floral accent, elevating any gift.",
                Price = 15.00m,
                Category = Cat("Words"),
                SubCategory = "Gift Wrapping",
                ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuCXOMgfLAN299HXnFTq5MqxB_JFn9gIPvH2gwpSxwJ1jFNAnV7L4a10lo-nKWnD77PQUZ2JjaMh05XAhwY2a8uGuG9ntK79BgnoaQ7hQzdApuMtvig_v0H-4_7bNQ2eIRmW1qk1B5iJycrwhQlDg1A-p6ivT99YOhCNHkTwWS-VakfewSowim9jinZdq9JVt_V-EBoefcoBGuJBCg5Y5uXdSP4socWkyXbGmwTGjZPvwxgpAR2pGDUsdntMt0HHpPcxNzozVtyz5dI",
                Materials =
                [
                    new() { Icon = "eco", Name = "Japanese Washi Paper" },
                    new() { Icon = "spa", Name = "Dried Floral Accent" },
                    new() { Icon = "grass", Name = "Natural Twine" },
                    new() { Icon = "recycling", Name = "Recycled Tissue Lining" }
                ],
                Features =
                [
                    new() { Icon = "eco", Title = "Plastic-Free", Description = "Every element is compostable or fully biodegradable." },
                    new() { Icon = "palette", Title = "Artisan Folded", Description = "Hand-folded for clean, elegant presentation every time." },
                    new() { Icon = "auto_awesome", Title = "Dried Floral Topper", Description = "Each wrap finished with a unique dried botanical accent." },
                    new() { Icon = "recycling", Title = "100% Compostable", Description = "Kind to the earth before and after the unwrapping moment." }
                ],
                StoryTitle = "The Art of First Impressions",
                StoryDescription = "Sustainable Washi paper with a dried floral accent so beautiful, recipients have been known to frame it. Wrapping that earns its own applause."
            },
            new()
            {
                Name = "Morning Meadow",
                Description = "A vibrant seasonal wildflower bouquet capturing the essence of a fresh morning meadow.",
                Price = 55.00m,
                Category = Cat("Flora"),
                SubCategory = "Fresh Bouquets",
                ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuDmzy6t1dSZcdLn3nH-K_eIQEQVM0tHSW8OtotNY-BrMMoVo5vmarWAAtGilqv7tz04b5E8bKzrgoRT4ZrPvSdP_jjm_fbHjQRxK5rUOae2DoUEmxyeelcBCahqkfJKoHi7OU2p7JgEuhXBa4cH_7MLmCQqvCIHhCIFzH2-lQa0maPmWUNegRLI5oq5D97_mCK_rOz9a2hLW8CFvfXefkcBzankUXgwkD_fpAfwIpdDGn337E6DdOccUrhX5o_iJenZfqNxXBfIfsQ",
                Materials =
                [
                    new() { Icon = "spa", Name = "Seasonal Wildflowers" },
                    new() { Icon = "grass", Name = "Mixed Meadow Stems" },
                    new() { Icon = "water_drop", Name = "Hydration Pack" },
                    new() { Icon = "eco", Name = "Biodegradable Wrap" }
                ],
                Features =
                [
                    new() { Icon = "wb_sunny", Title = "Seasonally Sourced", Description = "Flowers chosen for what's freshest and most vibrant right now." },
                    new() { Icon = "local_florist", Title = "Wild-Inspired", Description = "Arranged to mimic the natural abundance of a summer meadow." },
                    new() { Icon = "water_drop", Title = "Hydration Pack Included", Description = "Built-in water pouch keeps stems fresh on their journey." },
                    new() { Icon = "eco", Title = "Zero Waste Packaging", Description = "No plastic, no waste — just flowers and joy." }
                ],
                StoryTitle = "Bottled Sunshine",
                StoryDescription = "A seasonal wildflower bouquet that smells like a good mood. Changes with the seasons because perfection is never quite the same twice."
            },
            new()
            {
                Name = "Silver Dollar",
                Description = "A fresh eucalyptus bundle with the natural silvery-green tones that bring calm to any space.",
                Price = 35.00m,
                Category = Cat("Flora"),
                SubCategory = "Single Stems",
                ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuCXOMgfLAN299HXnFTq5MqxB_JFn9gIPvH2gwpSxwJ1jFNAnV7L4a10lo-nKWnD77PQUZ2JjaMh05XAhwY2a8uGuG9ntK79BgnoaQ7hQzdApuMtvig_v0H-4_7bNQ2eIRmW1qk1B5iJycrwhQlDg1A-p6ivT99YOhCNHkTwWS-VakfewSowim9jinZdq9JVt_V-EBoefcoBGuJBCg5Y5uXdSP4socWkyXbGmwTGjZPvwxgpAR2pGDUsdntMt0HHpPcxNzozVtyz5dI",
                Materials =
                [
                    new() { Icon = "grass", Name = "Silver Dollar Eucalyptus" },
                    new() { Icon = "spa", Name = "Aromatic Essential Oils" },
                    new() { Icon = "water_drop", Name = "Hydration Band" },
                    new() { Icon = "eco", Name = "Natural Twine" }
                ],
                Features =
                [
                    new() { Icon = "spa", Title = "Aromatherapy Grade", Description = "Natural oils release a calming, clean scent as the stems warm." },
                    new() { Icon = "water_drop", Title = "Long-Lasting Freshness", Description = "Stays vibrant and aromatic for up to three weeks in water." },
                    new() { Icon = "eco", Title = "Pesticide-Free", Description = "Grown without chemical pesticides for a truly natural experience." },
                    new() { Icon = "wb_sunny", Title = "Shower-Ready", Description = "Hang from the shower head to activate essential oils with steam." }
                ],
                StoryTitle = "The Quiet Room",
                StoryDescription = "Fresh eucalyptus that transforms any space into a spa without the awkward small talk. Hang in the shower for an instant sensory upgrade."
            },
            new()
            {
                Name = "Pure Serenity",
                Description = "Imperial white lilies arranged with quiet elegance for moments of deep feeling.",
                Price = 72.00m,
                Category = Cat("Flora"),
                SubCategory = "Fresh Bouquets",
                ImageUrl = "https://images.unsplash.com/photo-1487530811176-3780de880c2d",
                Materials =
                [
                    new() { Icon = "spa", Name = "Imperial White Lilies" },
                    new() { Icon = "grass", Name = "Seasonal Foliage" },
                    new() { Icon = "water_drop", Name = "Flower Food Sachet" },
                    new() { Icon = "eco", Name = "Recycled Kraft Wrap" }
                ],
                Features =
                [
                    new() { Icon = "local_florist", Title = "Expert Arrangement", Description = "Assembled by florists trained in classic European flower design." },
                    new() { Icon = "water_drop", Title = "Conditioning Sachet", Description = "Included flower food extends vase life significantly." },
                    new() { Icon = "wb_sunny", Title = "Long Vase Life", Description = "With care, these lilies will bloom for up to ten days." },
                    new() { Icon = "eco", Title = "Plastic-Free Wrapping", Description = "Presented in recycled kraft paper, tied with natural twine." }
                ],
                StoryTitle = "Silence in Bloom",
                StoryDescription = "Imperial white lilies arranged with quiet elegance. So perfectly composed they've been known to hush a room without saying a single word."
            },
            new()
            {
                Name = "Blush Peony",
                Description = "Soft pink double blooms arranged to capture the full, lush beauty of the peony.",
                Price = 78.00m,
                Category = Cat("Flora"),
                SubCategory = "Fresh Bouquets",
                ImageUrl = "https://images.unsplash.com/photo-1582794543139-8ac9cb0f7b11",
                Materials =
                [
                    new() { Icon = "spa", Name = "Pink Double Peonies" },
                    new() { Icon = "grass", Name = "Italian Ruscus" },
                    new() { Icon = "water_drop", Name = "Flower Food Sachet" },
                    new() { Icon = "eco", Name = "Biodegradable Wrap" }
                ],
                Features =
                [
                    new() { Icon = "local_florist", Title = "Hand-Selected Blooms", Description = "Each stem chosen for fullness and colour at peak condition." },
                    new() { Icon = "water_drop", Title = "Bloom-Ready Buds", Description = "Delivered in bud form to open beautifully over five to seven days." },
                    new() { Icon = "wb_sunny", Title = "Extended Vase Life", Description = "Peonies open slowly, giving a full week of evolving beauty." },
                    new() { Icon = "eco", Title = "Plastic-Free Packaging", Description = "All wrapping is compostable and free of single-use plastics." }
                ],
                StoryTitle = "The Full Blush",
                StoryDescription = "Double blooms so lush they once made a minimalist reconsider everything. Soft pink, full of life, and completely unapologetic about taking up space."
            },
            new()
            {
                Name = "Letterpress Card",
                Description = "Letterpress-printed stationery on thick cotton paper, for words that deserve a beautiful vessel.",
                Price = 12.00m,
                Category = Cat("Words"),
                SubCategory = "Cards",
                ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuCL_ybLvB2bNntq94J5CiclR-h6BpYOmeyCzbDcsDbqkB5-BxZxhcGknI0FZdBApHBmvlV0kjLaxiinPfNg8iA9zFpujTBu-OazaWiId28TY5iWSm1_49JG1YVbHfLUyk6O2DS7nY0Auf7Lq6DGAfolkuzYiqZHKOpVV6Ix11D8_D7rjN2QawcBbGLDQ2N9UHEPnM8ddW-W5CNQGp0OpmQNfWYf_cdlwBLVrVNQinqyIGClL4_WWMzl7_i6SyPIKAVmtzDdu9WMqw8",
                Materials =
                [
                    new() { Icon = "eco", Name = "280gsm Cotton Paper" },
                    new() { Icon = "palette", Name = "Letterpress Ink" },
                    new() { Icon = "spa", Name = "Linen Envelope" },
                    new() { Icon = "recycling", Name = "FSC-Certified Materials" }
                ],
                Features =
                [
                    new() { Icon = "palette", Title = "Letterpress Printed", Description = "Each card individually pressed, leaving a tactile impression in the paper." },
                    new() { Icon = "workspace_premium", Title = "Cotton Card Stock", Description = "Thick cotton paper with a luxurious weight and texture." },
                    new() { Icon = "auto_awesome", Title = "Blank Inside", Description = "Unlined interior leaves full space for your own heartfelt words." },
                    new() { Icon = "eco", Title = "Sustainably Sourced", Description = "FSC-certified materials from responsibly managed forests." }
                ],
                StoryTitle = "Press to Impress",
                StoryDescription = "Printed with a 19th-century technique on cotton paper thick enough to lean against. For words that deserve to be felt as well as read."
            },
            new()
            {
                Name = "Dark Chocolate Truffle Collection",
                Description = "Assorted dark chocolate truffles dusted with cocoa powder and crushed nuts, a sophisticated indulgence.",
                Price = 52.00m,
                Category = Cat("Cacao"),
                SubCategory = "Premium Collection",
                ImageUrl = "https://plus.unsplash.com/premium_photo-1667031518595-9cb4b0d504ef",
                Materials =
                [
                    new() { Icon = "spa", Name = "85% Dark Couverture" },
                    new() { Icon = "eco", Name = "Premium Cocoa Powder" },
                    new() { Icon = "grass", Name = "Crushed Mixed Nuts" },
                    new() { Icon = "water_drop", Name = "Pure Cocoa Butter" }
                ],
                Features =
                [
                    new() { Icon = "workspace_premium", Title = "Premium Couverture", Description = "Made with 85% Belgian dark couverture for a deep, complex flavour." },
                    new() { Icon = "star", Title = "Award-Winning Recipe", Description = "Developed by a chocolatier with three international awards and strong opinions about tempering." },
                    new() { Icon = "eco", Title = "All-Natural Dusting", Description = "Finished with natural cocoa powder and unsalted crushed nuts." },
                    new() { Icon = "palette", Title = "Gift Boxed", Description = "Presented in a keepsake box, ready to gift without extra wrapping." }
                ],
                StoryTitle = "Dark and Accomplished",
                StoryDescription = "Fourteen truffles dusted with cocoa and crushed nuts. Sophisticated enough for a dinner party, dangerous enough to disappear before it starts."
            },
            new()
            {
                Name = "Salted Caramel Bonbons",
                Description = "Smooth milk chocolate shells filled with tangy salted caramel and a touch of sea salt from Brittany.",
                Price = 38.00m,
                Category = Cat("Cacao"),
                SubCategory = "Flavor Creations",
                ImageUrl = "https://images.unsplash.com/photo-1679143121627-4dba2860ef50",
                Materials =
                [
                    new() { Icon = "spa", Name = "Milk Chocolate Shell" },
                    new() { Icon = "water_drop", Name = "Salted Caramel Filling" },
                    new() { Icon = "eco", Name = "Brittany Sea Salt" },
                    new() { Icon = "grass", Name = "Pure Cream" }
                ],
                Features =
                [
                    new() { Icon = "palette", Title = "Hand-Moulded", Description = "Each bonbon individually shaped for a perfect chocolate-to-filling ratio." },
                    new() { Icon = "water_drop", Title = "Soft Caramel Centre", Description = "Slow-cooked caramel achieves a perfectly fluid, buttery texture." },
                    new() { Icon = "star", Title = "Sea Salt Finish", Description = "A flake of Brittany sea salt balances the sweetness with precision." },
                    new() { Icon = "workspace_premium", Title = "Small Batch Made", Description = "Produced weekly in small batches to guarantee freshness." }
                ],
                StoryTitle = "Sweet, Salty, Sorted",
                StoryDescription = "Milk chocolate shells with salted caramel centres and Brittany sea salt. Science has yet to explain why you always want just one more."
            },
            new()
            {
                Name = "Ruby Rose Chocolate",
                Description = "White chocolate infused with freeze-dried raspberries and rose petals, an elegant floral escape.",
                Price = 48.00m,
                Category = Cat("Cacao"),
                SubCategory = "Floral Infusions",
                ImageUrl = "https://images.unsplash.com/photo-1679143121360-d2f8e948e996",
                Materials =
                [
                    new() { Icon = "spa", Name = "White Chocolate" },
                    new() { Icon = "grass", Name = "Freeze-Dried Raspberries" },
                    new() { Icon = "eco", Name = "Dried Rose Petals" },
                    new() { Icon = "water_drop", Name = "Pure Vanilla Extract" }
                ],
                Features =
                [
                    new() { Icon = "palette", Title = "Artisan Crafted", Description = "Made in small batches by hand for consistent quality and visual appeal." },
                    new() { Icon = "eco", Title = "Real Botanicals", Description = "Genuine freeze-dried fruit and rose petals — no artificial flavouring." },
                    new() { Icon = "star", Title = "No Artificial Flavours", Description = "Every flavour comes from natural fruit and botanical sources." },
                    new() { Icon = "auto_awesome", Title = "Gift-Ready Presentation", Description = "Packaged in a gift-ready sleeve with botanical illustration." }
                ],
                StoryTitle = "The Garden in a Bar",
                StoryDescription = "White chocolate with freeze-dried raspberries and real rose petals. Floral, fruity, and far too beautiful to eat — until you inevitably do."
            },
            new()
            {
                Name = "Single-Origin Ecuador",
                Description = "70% dark chocolate from Ecuador's finest cacao farms, with bright citrus notes and subtle earthiness.",
                Price = 28.00m,
                Category = Cat("Cacao"),
                SubCategory = "Origin Stories",
                ImageUrl = "https://images.unsplash.com/photo-1682120501920-7ce18b00237a",
                Materials =
                [
                    new() { Icon = "spa", Name = "Ecuadorian Arriba Cacao" },
                    new() { Icon = "eco", Name = "Organic Cocoa Butter" },
                    new() { Icon = "grass", Name = "Unrefined Cane Sugar" },
                    new() { Icon = "water_drop", Name = "Pure Vanilla" }
                ],
                Features =
                [
                    new() { Icon = "eco", Title = "Single-Farm Sourced", Description = "All cacao traced to one cooperative in Ecuador's coastal region." },
                    new() { Icon = "workspace_premium", Title = "70% Cocoa Content", Description = "A bold, complex dark chocolate that rewards slow, considered eating." },
                    new() { Icon = "star", Title = "Tasting Notes Included", Description = "Each bar comes with a card describing the origin and flavour profile." },
                    new() { Icon = "verified", Title = "Fair Trade Certified", Description = "Farmers receive above-market prices and long-term trade commitments." }
                ],
                StoryTitle = "From the Rainforest, With Love",
                StoryDescription = "70% dark chocolate from Ecuador's finest farms. Citrus top notes, earthy finish, and the quiet satisfaction of knowing exactly where every bean was grown."
            },
            new()
            {
                Name = "Honey Lavender Ganache",
                Description = "Rich dark chocolate ganache sweetened with local honey and infused with fragrant lavender from Provence.",
                Price = 42.00m,
                Category = Cat("Cacao"),
                SubCategory = "Artisan Creations",
                ImageUrl = "https://plus.unsplash.com/premium_photo-1716152295684-21731e330e36",
                Materials =
                [
                    new() { Icon = "spa", Name = "Dark Chocolate Ganache" },
                    new() { Icon = "eco", Name = "Local Raw Honey" },
                    new() { Icon = "grass", Name = "Provençal Lavender" },
                    new() { Icon = "water_drop", Name = "Pure Cream" }
                ],
                Features =
                [
                    new() { Icon = "eco", Title = "Raw Honey Used", Description = "Unfiltered local honey preserves natural enzymes and floral notes." },
                    new() { Icon = "palette", Title = "Hand-Piped", Description = "The ganache is piped by hand into each shell for a flawless finish." },
                    new() { Icon = "star", Title = "Provençal Botanicals", Description = "Lavender sourced from Provence for its distinctively soft, sweet character." },
                    new() { Icon = "workspace_premium", Title = "Small Batch", Description = "Made to order in quantities small enough that quality is never compromised." }
                ],
                StoryTitle = "The Beekeeper's Secret",
                StoryDescription = "Dark chocolate ganache with local raw honey and Provençal lavender. Made by a chocolatier who once kept bees and has never quite got over the romance of it."
            },
            new()
            {
                Name = "Emerald Gemstone Ring",
                Description = "14k gold band set with a brilliant emerald-cut emerald, surrounded by delicate diamond accents.",
                Price = 385.00m,
                Category = Cat("Adornments"),
                SubCategory = "Statement Pieces",
                ImageUrl = "https://images.unsplash.com/photo-1689775703592-976824d76033",
                Materials =
                [
                    new() { Icon = "diamond", Name = "14k Gold Band" },
                    new() { Icon = "spa", Name = "Emerald-Cut Emerald" },
                    new() { Icon = "auto_awesome", Name = "Diamond Accents" },
                    new() { Icon = "eco", Name = "Conflict-Free Gemstones" }
                ],
                Features =
                [
                    new() { Icon = "verified", Title = "GIA Certified", Description = "Each stone comes with a Gemological Institute of America grading report." },
                    new() { Icon = "workspace_premium", Title = "14k Gold Hallmarked", Description = "Gold content verified and hallmarked for guaranteed purity." },
                    new() { Icon = "eco", Title = "Conflict-Free", Description = "All gemstones ethically sourced under the Kimberley Process." },
                    new() { Icon = "auto_awesome", Title = "Handset Stones", Description = "Each diamond accent individually placed and secured by a master setter." }
                ],
                StoryTitle = "The Green That Means Go",
                StoryDescription = "An emerald-cut emerald that commands attention without saying a word. Set in 14k gold with diamond accents, for people who consider subtlety overrated."
            },
            new()
            {
                Name = "Crystal Drops Earrings",
                Description = "Handcrafted gold earrings featuring lustrous South Sea crystals with delicate filigree details.",
                Price = 245.00m,
                Category = Cat("Adornments"),
                SubCategory = "Everyday Elegance",
                ImageUrl = "https://images.unsplash.com/photo-1665198134143-8c4434d3578b",
                Materials =
                [
                    new() { Icon = "spa", Name = "South Sea Pearls" },
                    new() { Icon = "diamond", Name = "18k Gold Setting" },
                    new() { Icon = "auto_awesome", Name = "Hand-Filigree Wire" },
                    new() { Icon = "eco", Name = "Hypoallergenic Posts" }
                ],
                Features =
                [
                    new() { Icon = "workspace_premium", Title = "Genuine South Sea Pearls", Description = "Cultured over several years for exceptional lustre and size." },
                    new() { Icon = "auto_awesome", Title = "Hand-Filigree Work", Description = "Gold wire shaped by hand into a delicate lattice frame." },
                    new() { Icon = "verified", Title = "Hallmarked Gold", Description = "18k gold content verified and hallmarked." },
                    new() { Icon = "spa", Title = "Lightweight Wear", Description = "Designed for all-day comfort without sacrificing elegance." }
                ],
                StoryTitle = "The Daily Treasure",
                StoryDescription = "South Sea pearls in gold filigree earrings light enough to forget you're wearing them, beautiful enough that others certainly won't."
            },
            new()
            {
                Name = "Vintage Locket Necklace",
                Description = "Rose gold vintage-inspired locket engraved with botanical details, perfect for holding precious memories.",
                Price = 165.00m,
                Category = Cat("Adornments"),
                SubCategory = "Timeless Keepsakes",
                ImageUrl = "https://images.unsplash.com/photo-1589128777073-263566ae5e4d",
                Materials =
                [
                    new() { Icon = "diamond", Name = "Rose Gold Plated Setting" },
                    new() { Icon = "spa", Name = "Engraved Botanical Detail" },
                    new() { Icon = "auto_awesome", Name = "Glass Locket Window" },
                    new() { Icon = "eco", Name = "Tarnish-Resistant Finish" }
                ],
                Features =
                [
                    new() { Icon = "palette", Title = "Engraved Botanicals", Description = "Botanical motifs hand-engraved on both faces of the locket." },
                    new() { Icon = "spa", Title = "Photo-Ready Interior", Description = "Velvet-lined interior holds two photographs securely." },
                    new() { Icon = "auto_awesome", Title = "Vintage-Style Clasp", Description = "Spring-ring closure inspired by Victorian jewellery design." },
                    new() { Icon = "workspace_premium", Title = "Rose Gold Finish", Description = "Rhodium-free plating gives a warm, enduring rose gold tone." }
                ],
                StoryTitle = "Where Memories Live",
                StoryDescription = "A rose gold locket engraved with botanical motifs, designed to hold what matters most. Fits two small photos or one very important secret."
            },
            new()
            {
                Name = "Sapphire Promise Bracelet",
                Description = "White gold bracelet adorned with deep blue sapphires alternating with brilliant white diamonds in a classic pattern.",
                Price = 425.00m,
                Category = Cat("Adornments"),
                SubCategory = "Luxury Collection",
                ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuAFTXBz1oBwjnwwCWSRlx8yw5epqJdFYBGoFDrSjirs2NJV6d1M4w_xeKzbZVOU7tsrnXfV8J_0loqf6U4_3Cc96Akyk3fvNsGYluAh93bDdMioiCVSoDj0vY6nu8og69QiRd8exPnK-AypYdLFp__jw52RyM0MWxwxqUOEslPXQ7je7Xu1GaDXIw17AFB2iArIn2bc-jU7XfPrXZ_N8CkVY51WDhviFUVQHQKxIYUcwuHc2k1szCdrUZUi1nIBbjn1q9hIfar6v60",
                Materials =
                [
                    new() { Icon = "diamond", Name = "18k White Gold" },
                    new() { Icon = "spa", Name = "Blue Sapphires" },
                    new() { Icon = "auto_awesome", Name = "White Diamonds" },
                    new() { Icon = "eco", Name = "Conflict-Free Stones" }
                ],
                Features =
                [
                    new() { Icon = "verified", Title = "Certified Gemstones", Description = "All sapphires and diamonds certified for quality and ethical origin." },
                    new() { Icon = "workspace_premium", Title = "18k White Gold", Description = "Rhodium-plated white gold for a bright, durable finish." },
                    new() { Icon = "auto_awesome", Title = "Alternating Stone Pattern", Description = "Sapphires and diamonds set in precise alternation by hand." },
                    new() { Icon = "spa", Title = "Adjustable Clasp", Description = "Lobster clasp with two adjustment links for a comfortable, secure fit." }
                ],
                StoryTitle = "The Promise Keeper",
                StoryDescription = "White gold set with deep blue sapphires and white diamonds in a pattern so timeless it predates fashion trends by several centuries."
            }
        ];
    }
}