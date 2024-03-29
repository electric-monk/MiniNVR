﻿using System;
using System.Collections.Generic;

namespace TestConsole.Onvif
{
    public class MediaLink : BaseService<Media.MediaClient, Media.Media>
    {
        public class Profile
        {
            internal readonly Media.Profile profile;

            internal Profile(Media.Profile data)
            {
                profile = data;
            }
        }

        internal MediaLink(Configuration.Cameras.Camera camera, string endpoint) : base(camera, endpoint) { }

        public Profile[] GetProfiles()
        {
            Media.Profile[] profiles = connection.GetProfiles();
            List<Profile> results = new List<Profile>();
            foreach (var profile in profiles)
                results.Add(new Profile(profile));
            return results.ToArray();
        }

        public Uri GetSnapshotUri(Profile profile)
        {
            Media.MediaUri uri = connection.GetSnapshotUri(profile.profile.token);
            return new Uri(uri.Uri);
        }

        public Uri GetStreamUri(Profile profile)
        {
            var setup = new Media.StreamSetup();
            setup.Stream = Media.StreamType.RTPUnicast;
            setup.Transport = new Media.Transport();
            setup.Transport.Protocol = Media.TransportProtocol.UDP;
            Media.MediaUri uri = connection.GetStreamUri(setup, profile.profile.token);
            return new Uri(uri.Uri);
        }
    }
}
