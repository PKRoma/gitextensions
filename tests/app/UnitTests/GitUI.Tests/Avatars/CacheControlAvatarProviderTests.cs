﻿using GitUI.Avatars;
using NSubstitute;

namespace GitUITests.Avatars
{
    [TestFixture]
    public class CacheControlAvatarProviderTests
    {
        [Test]
        public async Task ClearCache_is_passed_to_all_children()
        {
            IAvatarCacheCleaner cacheCleaner1 = Substitute.For<IAvatarCacheCleaner>();
            IAvatarCacheCleaner cacheCleaner2 = Substitute.For<IAvatarCacheCleaner>();
            IAvatarCacheCleaner cacheCleaner3 = Substitute.For<IAvatarCacheCleaner>();

            EventHandler cacheClearedEventHandler = Substitute.For<EventHandler>();

            MultiCacheCleaner cacheCleaner = new(cacheCleaner1, cacheCleaner2, cacheCleaner3);
            cacheCleaner.CacheCleared += cacheClearedEventHandler;

            await cacheCleaner.ClearCacheAsync();

            await cacheCleaner1.Received(1).ClearCacheAsync();
            await cacheCleaner2.Received(1).ClearCacheAsync();
            await cacheCleaner3.Received(1).ClearCacheAsync();

            cacheClearedEventHandler.Received(1)(cacheCleaner, EventArgs.Empty);
        }

        [Test]
        public void Construction_with_null_parameters_is_permitted()
        {
            ClassicAssert.Throws<ArgumentNullException>(() =>
            {
                new MultiCacheCleaner(null);
            });

            ClassicAssert.Throws<ArgumentNullException>(() =>
            {
                new MultiCacheCleaner(null);
            });

            ClassicAssert.Throws<ArgumentNullException>(() =>
            {
                new MultiCacheCleaner(null, null);
            });
        }
    }
}
